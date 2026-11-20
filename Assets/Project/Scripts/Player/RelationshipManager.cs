using System;
using UnityEngine;
using Game.Core;

namespace Game.Gameplay {
    // Владеет Trust/Fatigue и решает Lost/Dead по вероятностной формуле, пересчитываемой
    // раз в checkInterval секунд — не жёсткий скрипт. Ничего не знает про CompanionController
    // напрямую (см. комментарий в GameLifetimeScope про циклическую DI), только принимает
    // данные через ReportCompanionStatus и отдаёт события-решения наружу.
    public class RelationshipManager : MonoBehaviour, IRelationshipManager {
        [Header("Base values")]
        [SerializeField, Range(0f, 1f)] private float trust = 0.6f;
        [SerializeField, Range(0f, 1f)] private float fatigue = 0f;

        [Header("Fatigue rate")]
        [SerializeField] private bool fatigueAccumulationEnabled = false; // временно выключено на тест-проходе
        [SerializeField] private float fatigueGrowthPerSecond = 0.02f;
        [SerializeField] private float fatigueRecoveryPerSecond = 0.08f;

        [Header("Lost/Dead — ЗАВЫШЕНО для тестирования, снизить на баланс-проходе")]
        [SerializeField] private float checkInterval = 5f;
        [SerializeField, Range(0f, 1f)] private float trustThresholdForLost = 0.4f;
        [SerializeField] private float distanceThresholdForLost = 12f;
        [SerializeField, Range(0f, 1f)] private float lostChancePerCheck = 0.5f;
        [SerializeField, Range(0f, 1f)] private float deathChancePerCheck = 0.2f;
        [SerializeField, Range(0f, 1f)] private float recoveryChancePerCheck = 0.3f;
        // fatigueThresholdForLost — удалено, Lost больше не зависит от Fatigue

        private bool _isCompanionLost;
        private bool _isCompanionAtFire;
        private float _distanceToPlayer;
        private float _checkTimer;

        public float Trust => trust;
        public float Fatigue => fatigue;

        public event Action<float> OnTrustChanged;
        public event Action<float> OnFatigueChanged;
        public event Action OnCompanionLostTriggered;
        public event Action OnCompanionDeathTriggered;
        public event Action OnCompanionRecoveredTriggered;

        public void AddTrust(float delta) {
            trust = Mathf.Clamp01(trust + delta);
            OnTrustChanged?.Invoke(trust);
        }

        public void AddFatigue(float delta) {
            fatigue = Mathf.Clamp01(fatigue + delta);
            OnFatigueChanged?.Invoke(fatigue);
        }

        public void ReportCompanionStatus(bool isAtFire, float distanceToPlayer) {
            _isCompanionAtFire = isAtFire;
            _distanceToPlayer = distanceToPlayer;
        }

        private void Update() {
            if (fatigueAccumulationEnabled) TickFatigue();
            TickLostDeathCheck();
        }
        private void TickFatigue() {
            // Пока компаньон Lost или Dead — Fatigue не тикаем: там уже другая логика/конец истории,
            // не хотим, чтобы значение продолжало расти в фоне и путало отладку.
            if (_isCompanionLost) return;

            float delta = (_isCompanionAtFire ? fatigueRecoveryPerSecond : -fatigueGrowthPerSecond)
                          * Time.deltaTime;
            AddFatigue(delta);
        }

        private void TickLostDeathCheck() {
            _checkTimer += Time.deltaTime;
            if (_checkTimer < checkInterval) return;
            _checkTimer = 0f;

            if (!_isCompanionLost) {
                bool conditionsMet = trust < trustThresholdForLost
                                      && _distanceToPlayer > distanceThresholdForLost;

                if (conditionsMet && Roll(lostChancePerCheck)) {
                    _isCompanionLost = true;
                    OnCompanionLostTriggered?.Invoke();
                }
                return;
            }

            if (Roll(deathChancePerCheck)) {
                OnCompanionDeathTriggered?.Invoke();
            } else if (Roll(recoveryChancePerCheck)) {
                _isCompanionLost = false;
                OnCompanionRecoveredTriggered?.Invoke();
            }
        }

        private static bool Roll(float chance) => UnityEngine.Random.value < chance;
    }
}