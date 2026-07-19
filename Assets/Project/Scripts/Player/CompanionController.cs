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
        [Tooltip("После этого порога Fatigue компаньон иногда отказывается идти вовсе (стоит на месте).")]
        [SerializeField, Range(0f, 1f)] private float refusalFatigueThreshold = 0.85f;
        [SerializeField, Range(0f, 1f)] private float refusalChancePerCheck = 0.3f;
        [SerializeField] private float refusalCheckInterval = 2f;

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
            float fatigue = _relationshipManager.Fatigue;
            _agent.speed = baseAgentSpeed * (1f - fatigue * fatigueSpeedPenalty);

            _refusalCheckTimer += Time.deltaTime;
            if (_refusalCheckTimer >= refusalCheckInterval) {
                _refusalCheckTimer = 0f;
                _isRefusingThisTick = fatigue >= refusalFatigueThreshold
                                       && UnityEngine.Random.value < refusalChancePerCheck;
            }
        }

        private void TickFollow(float distanceToPlayer) {
            if (distanceToPlayer > returnToFireThreshold && fireTransform != null) {
                _state = CompanionState.ReturnToFire;
                _agent.SetDestination(fireTransform.position);
                return;
            }

            if (_isRefusingThisTick) {
                _agent.ResetPath(); // "отказ идти" — Fatigue слишком высокая
                return;
            }

            if (distanceToPlayer > followDistance) {
                _agent.SetDestination(_player.transform.position);
            } else {
                _agent.ResetPath();
            }
        }

        private void TickReturnToFire() {
            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance) {
                _state = CompanionState.Waiting;
                _waitingTimer = 0f;
                _waitingDurationTarget = UnityEngine.Random.Range(minWaitingDuration, maxWaitingDuration);
            }
        }

        private void TickWaiting(float distanceToPlayer) {
            _waitingTimer += Time.deltaTime;

            bool minTimeElapsed = _waitingTimer >= _waitingDurationTarget;
            bool playerCameBack = distanceToPlayer <= resumeFollowDistance;

            // Возвращаемся к Follow, только если оба условия выполнены — не просто по таймеру
            // (иначе компаньон уйдёт от костра, даже если героиня всё ещё далеко) и не просто
            // по приближению игрока (иначе не будет того самого "минимального отдыха").
            if (minTimeElapsed && playerCameBack) {
                _state = CompanionState.Follow;
            }
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
            _agent.ResetPath();
            _agent.enabled = false;

            OnDied?.Invoke();
        }

        private void HandleRecoveredTriggered() {
            if (_state != CompanionState.Lost) return; // защита от гонки состояний
            _state = fireTransform != null ? CompanionState.ReturnToFire : CompanionState.Follow;
            if (fireTransform != null)
                _agent.SetDestination(fireTransform.position);
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
    }
}