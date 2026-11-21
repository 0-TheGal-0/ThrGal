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
        // Explore, Resting — пока не нужны для текущих задач, добавим по мере необходимости.
    }

    [RequireComponent(typeof(NavMeshAgent))]
    public class CompanionController : MonoBehaviour, ICompanionController {
        [Header("Follow")]
        [SerializeField] private float followDistance = 3f;
        [SerializeField] private float returnToFireThreshold = 12f;
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
        [Tooltip("При Fatigue = 1 скорость падает на эту долю от базовой.")]
        [SerializeField, Range(0f, 1f)] private float fatigueSpeedPenalty = 0.6f;
        // refusalFatigueThreshold / refusalChancePerCheck / refusalCheckInterval — удалены

        [Header("Lost")]
        [Tooltip("Радиус поиска Dark-чанка вокруг текущей позиции компаньона при срабатывании Lost.")]
        [SerializeField] private int lostSearchRadiusInChunks = 5;

        private NavMeshAgent _agent;
        private PlayerController _player;
        private IRelationshipManager _relationshipManager;
        private IChunkManager _chunkManager;

        private CompanionState _state = CompanionState.Follow;
        private float _waitingTimer;
        private float _waitingDurationTarget;
        private float _refusalCheckTimer;
        private bool _isRefusingThisTick;

        public Vector3 Position => transform.position;
        public bool IsAtFire => _state == CompanionState.Waiting;

        public event Action OnLost;
        public event Action OnDied;

        [Inject]
        public void Construct(PlayerController player, IRelationshipManager relationshipManager,
            IChunkManager chunkManager) {
            _player = player;
            _relationshipManager = relationshipManager;
            _chunkManager = chunkManager;
        }

        private void Awake() {
            _agent = GetComponent<NavMeshAgent>();
        }

        private void OnEnable() {
            _relationshipManager.OnCompanionLostTriggered += HandleLostTriggered;
            _relationshipManager.OnCompanionDeathTriggered += HandleDeathTriggered;
            _relationshipManager.OnCompanionRecoveredTriggered += HandleRecoveredTriggered;
        }

        private void OnDisable() {
            _relationshipManager.OnCompanionLostTriggered -= HandleLostTriggered;
            _relationshipManager.OnCompanionDeathTriggered -= HandleDeathTriggered;
            _relationshipManager.OnCompanionRecoveredTriggered -= HandleRecoveredTriggered;
        }

        private void Update() {
            if (_state == CompanionState.Dead) return; // финальное состояние, дальше ничего не тикаем

            float distanceToPlayer = Vector3.Distance(transform.position, _player.transform.position);
            _relationshipManager.ReportCompanionStatus(_state == CompanionState.Waiting, distanceToPlayer);

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
                    // Пока Lost — компаньон стоит на месте (агент уже остановлен в HandleLostTriggered).
                    // Выход отсюда управляется только событиями RelationshipManager (Recovered/Death).
                    break;
            }
        }

        private void ApplyFatigueToAgent() {
            float fatigue01 = _relationshipManager.Fatigue / RelationshipScale.Max;
            _agent.speed = baseAgentSpeed * (1f - fatigue01 * fatigueSpeedPenalty);
            // блок с _refusalCheckTimer / _isRefusingThisTick — удалён целиком
        }

        private void TickFollow(float distanceToPlayer) {
            if (distanceToPlayer > returnToFireThreshold && fireTransform != null) {
                _state = CompanionState.ReturnToFire;
                _agent.SetDestination(fireTransform.position);
                return;
            }

            if (distanceToPlayer > followDistance) {
                _agent.SetDestination(_player.transform.position);
            } else {
                _agent.ResetPath();
            }
            // проверка _isRefusingThisTick — удалена
        }

        private void TickReturnToFire() {
            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance) {
                _state = CompanionState.Waiting;
                _waitingTimer = 0f;
                _waitingDurationTarget = UnityEngine.Random.Range(minWaitingDuration, maxWaitingDuration);
            }
        }

        private void TickWaiting(float distanceToPlayer) {
            // Автоперехода больше нет — девочка стоит у костра, пока Follow не включат
            // вручную через контекстное меню компонента (см. ForceFollow ниже).
        }

        private void HandleLostTriggered() {
            _state = CompanionState.Lost;
            _agent.ResetPath();

            if (TryFindNearbyDarkChunk(out Vector3 darkPosition)) {
                _agent.Warp(darkPosition);
            }
            // Если Dark-чанк рядом не нашёлся — компаньон просто остаётся стоять там, где был.

            OnLost?.Invoke();
        }

        private void HandleDeathTriggered() {
            _state = CompanionState.Dead;
            //_agent.ResetPath();
            //Вызывает вылет
            _agent.enabled = false;

            OnDied?.Invoke();
        }

        private void HandleRecoveredTriggered() {
            if (_state != CompanionState.Lost) return;
            // Раньше здесь был автопереход в ReturnToFire/Follow. Теперь просто "приходит в себя"
            // и стоит на месте — дальше управляешь вручную через ForceFollow.
            _state = CompanionState.Waiting;
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
        // Доступно через кнопку ⋮ (три точки) на компоненте CompanionController в инспекторе
        // во время Play Mode. Временный ручной контроль над Follow, пока не спроектирована
        // логика автовозврата (анимации, реакция на приближение игрока и т.д. — отдельная задача).
        [ContextMenu("Force Follow")]
        private void ForceFollow() {
            if (_state == CompanionState.Dead) return;
            _state = CompanionState.Follow;
            _agent.enabled = true;
            _agent.ResetPath();
        }
    }
}