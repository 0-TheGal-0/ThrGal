using System;
using UnityEngine;
using UnityEngine.AI;
using VContainer;
using Game.Core;

namespace Game.AI {
    public enum CompanionState {
        Follow,
        ReturnToFire,
        Waiting,
        Lost,
        Dead
        // Explore, Resting — добавим, когда появится логика у костра и безопасные зоны.
    }

    [RequireComponent(typeof(NavMeshAgent))]
    public class CompanionController : MonoBehaviour, ICompanionController {
        [Header("Follow")]
        [SerializeField] private float followDistance = 3f;
        [Tooltip("Порог отставания = радиус фонаря героини × этот множитель.")]
        [SerializeField] private float returnToFireRadiusMultiplier = 1.5f;
        [Tooltip("Порог, если свет героини не найден в реестре.")]
        [SerializeField] private float fallbackReturnToFireThreshold = 12f;
        [SerializeField] private Transform fireTransform;

        [Header("Waiting")]
        [Tooltip("Минимальное время у костра, прежде чем компаньон снова готов идти за героиней — " +
                 "даже если Fatigue уже восстановилась. Не даёт девочке 'отскакивать' обратно мгновенно.")]
        [SerializeField] private float minWaitingDuration = 4f;
        [SerializeField] private float maxWaitingDuration = 10f;
        [Tooltip("На каком расстоянии героиня должна снова подойти, чтобы компаньон заметил и пошёл следом.")]
        [SerializeField] private float resumeFollowDistance = 6f;

        [Header("Fatigue effect")]
        [SerializeField] private float baseAgentSpeed = 3.5f;
        [Tooltip("При Fatigue = 100 скорость падает на эту долю от базовой.")]
        [SerializeField, Range(0f, 1f)] private float fatigueSpeedPenalty = 0.6f;

        [Header("Lost")]
        [Tooltip("Радиус поиска Dark-чанка вокруг текущей позиции компаньона при срабатывании Lost.")]
        [SerializeField] private int lostSearchRadiusInChunks = 5;

        [Header("Debug (временное)")]
        [SerializeField] private bool logTransitions = true;

        private NavMeshAgent _agent;
        private PlayerController _player;
        private IRelationshipManager _relationshipManager;
        private IChunkManager _chunkManager;
        private ILightRegistry _lightRegistry;

        private CompanionState _state = CompanionState.Follow;
        private float _waitingTimer;
        private float _waitingDurationTarget;
        private float _lostTimer;
        private float _lostDuration;
        private LightSourceHandle _playerLight;

        public Vector3 Position => transform.position;
        public bool IsAtFire => _state == CompanionState.Waiting;

        public event Action OnLost;
        public event Action OnDied;

        [Inject]
        public void Construct(PlayerController player, IRelationshipManager relationshipManager,
            IChunkManager chunkManager, ILightRegistry lightRegistry) {
            _player = player;
            _relationshipManager = relationshipManager;
            _chunkManager = chunkManager;
            _lightRegistry = lightRegistry;
        }

        private void Awake() {
            _agent = GetComponent<NavMeshAgent>();
        }

        // Порог отставания: радиус фонаря героини + 50%. Читаем живое значение из реестра,
        // поэтому апгрейды света (LightSource.SetRadius) подхватываются сами.
        private float ReturnToFireThreshold {
            get {
                if (_playerLight != null && _playerLight.Source == null) _playerLight = null;
                if (_playerLight == null) _playerLight = FindPlayerLight();
                return _playerLight != null
                    ? _playerLight.Radius * returnToFireRadiusMultiplier
                    : fallbackReturnToFireThreshold;
            }
        }

        private LightSourceHandle FindPlayerLight() {
            if (_lightRegistry == null || _player == null) return null;
            var sources = _lightRegistry.ActiveSources;
            for (int i = 0; i < sources.Count; i++) {
                var handle = sources[i];
                if (handle.Source != null && handle.Source.IsChildOf(_player.transform)) return handle;
            }
            return null;
        }

        private void Update() {
            if (_state == CompanionState.Dead) return; // финальное состояние, дальше ничего не тикаем

            float distanceToPlayer = Vector3.Distance(transform.position, _player.transform.position);
            // Нужно накоплению Fatigue (когда его включат); логика Lost/Death теперь здесь.
            _relationshipManager.ReportCompanionStatus(
                _state == CompanionState.Waiting, _state == CompanionState.Lost);

            ApplyFatigueToAgent();

            switch (_state) {
                case CompanionState.Follow:
                    TickFollow(distanceToPlayer);
                    break;
                case CompanionState.ReturnToFire:
                    TickReturnToFire();
                    break;
                case CompanionState.Waiting:
                    TickWaiting(distanceToPlayer);
                    break;
                case CompanionState.Lost:
                    TickLost();
                    break;
            }
        }

        private void ApplyFatigueToAgent() {
            float fatigue01 = _relationshipManager.Fatigue / RelationshipScale.Max;
            _agent.speed = baseAgentSpeed * (1f - fatigue01 * fatigueSpeedPenalty);
        }

        // Единственный шлюз: имеет ли смысл вообще проверять отставание. Сейчас — только в Follow.
        // Сюда позже добавятся: "не в безопасной (освещённой) зоне", режим Explore и т.п.
        private bool CanFallBehind() {
            return _state == CompanionState.Follow && fireTransform != null;
        }

        private void TickFollow(float distanceToPlayer) {
            float threshold = ReturnToFireThreshold;
            if (CanFallBehind() && distanceToPlayer > threshold) {
                ResolveFallBehind(distanceToPlayer, threshold);
                return;
            }

            if (distanceToPlayer > followDistance) {
                _agent.SetDestination(_player.transform.position);
            } else {
                StopAgent();
            }
        }

        // Одна проверка на отставание. Любой исход выводит её из Follow — повторных бросков нет.
        private void ResolveFallBehind(float distance, float threshold) {
            float lostChance = _relationshipManager.LostChance;
            bool lost = UnityEngine.Random.value < lostChance;
            Log($"отстала: дистанция {distance:0.#} > порог {threshold:0.#}; " +
                $"шанс Lost {lostChance:P0} → {(lost ? "ВЫПАЛО" : "не выпало")}");

            if (lost) BeginLost();
            else BeginReturnToFire();
        }

        private void BeginReturnToFire() {
            SetState(CompanionState.ReturnToFire);
            _agent.SetDestination(fireTransform.position);
        }

        private void TickReturnToFire() {
            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance) {
                EnterRest();
            }
        }

        // Все переходы "исследование → отдых" проходят здесь: сюда же позже встанет логика у костра.
        private void EnterRest() {
            SetState(CompanionState.Waiting);
            _waitingTimer = 0f;
            _waitingDurationTarget = UnityEngine.Random.Range(minWaitingDuration, maxWaitingDuration);
        }

        // Все переходы "отдых → исследование" проходят здесь.
        private void ExitRest() {
            SetState(CompanionState.Follow);
            _agent.enabled = true;
            StopAgent();
        }

        private void TickWaiting(float distanceToPlayer) {
            // Автоперехода нет — девочка стоит у костра, пока Follow не включат вручную
            // через контекстное меню компонента (см. ForceFollow ниже).
        }

        private void BeginLost() {
            SetState(CompanionState.Lost);
            StopAgent();

            if (TryFindNearbyDarkChunk(out Vector3 darkPosition)) {
                _agent.Warp(darkPosition);
            }
            // Если Dark-чанк рядом не нашёлся — компаньон просто остаётся стоять там, где был.

            _lostTimer = 0f;
            _lostDuration = _relationshipManager.RollLostDuration();
            Log($"Lost на {_lostDuration:0.#} с");

            OnLost?.Invoke();
        }

        private void TickLost() {
            _lostTimer += Time.deltaTime;
            if (_lostTimer < _lostDuration) return;

            float deathChance = _relationshipManager.DeathChance;
            bool dies = UnityEngine.Random.value < deathChance;
            Log($"Lost закончился; шанс Death {deathChance:P1} → {(dies ? "ВЫПАЛО" : "не выпало")}");

            if (dies) Die();
            else BeginReturnToFire();
        }

        private void Die() {
            SetState(CompanionState.Dead);
            StopAgent();
            _agent.enabled = false;
            OnDied?.Invoke();
        }

        private bool TryFindNearbyDarkChunk(out Vector3 worldPosition) {
            var currentCoord = _chunkManager.WorldToChunkCoord(transform.position);

            for (int x = -lostSearchRadiusInChunks; x <= lostSearchRadiusInChunks; x++) {
                for (int z = -lostSearchRadiusInChunks; z <= lostSearchRadiusInChunks; z++) {
                    var coord = new Vector2Int(currentCoord.x + x, currentCoord.y + z);
                    if (_chunkManager.GetChunkState(coord) != ChunkState.Dark) continue;

                    Vector3 candidate = _chunkManager.ChunkToWorldCenter(coord);
                    if (NavMesh.SamplePosition(candidate, out var hit, 5f, NavMesh.AllAreas)) {
                        worldPosition = hit.position;
                        return true;
                    }
                }
            }

            worldPosition = default;
            return false;
        }

        // ResetPath допустим только у включённого агента, стоящего на NavMesh.
        private void StopAgent() {
            if (_agent.enabled && _agent.isOnNavMesh) _agent.ResetPath();
        }

        private void SetState(CompanionState newState) {
            if (_state == newState) return;
            Log($"{_state} → {newState}");
            _state = newState;
        }

        private void Log(string message) {
            if (logTransitions) Debug.Log($"[Companion] {message}");
        }

        // Доступно через кнопку ⋮ на компоненте CompanionController в инспекторе во время Play Mode.
        // Временный ручной контроль, пока не спроектирована логика автовозврата.
        [ContextMenu("Force Follow")]
        private void ForceFollow() {
            if (_state == CompanionState.Dead) return;
            ExitRest();
        }

        // Временная отладка: порог отставания вокруг героини (виден при выделенной девочке).
        private void OnDrawGizmosSelected() {
            if (_player == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_player.transform.position, ReturnToFireThreshold);
        }
    }
}