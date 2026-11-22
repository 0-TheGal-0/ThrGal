using System;
using UnityEngine;
using Game.Core;

namespace Game.Gameplay {
    // Владеет Trust/Fatigue и считает от них риск (0..1), из которого выводятся шансы
    // Lost/Death и длительность Lost. Сам кубики не бросает: решения принимает
    // CompanionController. Ничего не знает про CompanionController напрямую (см. комментарий
    // в GameLifetimeScope про циклическую DI), только принимает данные через
    // ReportCompanionStatus.
    public class RelationshipManager : MonoBehaviour, IRelationshipManager {
        [Header("Base values (0–100)")]
        [SerializeField, Range(0f, RelationshipScale.Max)] private float trust = 60f;
        [SerializeField, Range(0f, RelationshipScale.Max)] private float fatigue = 0f;

        [Header("Fatigue rate (единиц в секунду, шкала 0–100)")]
        [SerializeField] private bool fatigueAccumulationEnabled = false;
        [SerializeField] private float fatigueGrowthPerSecond = 2f;
        [SerializeField] private float fatigueRecoveryPerSecond = 8f;

        [Header("Risk")]
        [Tooltip("Относительный вес низкого Trust в риске. Важна только пропорция с весом Fatigue.")]
        [SerializeField, Min(0f)] private float trustWeight = 0.6f;
        [SerializeField, Min(0f)] private float fatigueWeight = 0.4f;

        [Header("Lost")]
        [SerializeField, Range(0f, 1f)] private float lostChanceMin = 0.05f; // при риске 0
        [SerializeField, Range(0f, 1f)] private float lostChanceMax = 0.7f;  // при риске 1
        [Tooltip("Диапазон длительности Lost в секундах при риске 0 (x = мин, y = макс).")]
        [SerializeField] private Vector2 lostDurationAtMinRisk = new Vector2(5f, 10f);
        [Tooltip("То же при риске 1.")]
        [SerializeField] private Vector2 lostDurationAtMaxRisk = new Vector2(20f, 40f);

        [Header("Death")]
        [SerializeField, Range(0f, 1f)] private float deathChanceMin = 0f;
        [SerializeField, Range(0f, 1f)] private float deathChanceMax = 0.05f;

        private bool _isCompanionAtFire;
        private bool _isCompanionLostOrDead;

        public float Trust => trust;
        public float Fatigue => fatigue;

        public event Action<float> OnTrustChanged;
        public event Action<float> OnFatigueChanged;

        public float Risk {
            get {
                float sum = trustWeight + fatigueWeight;
                if (sum <= 0f) return 0f;
                float lowTrust = 1f - trust / RelationshipScale.Max;
                float fatigue01 = fatigue / RelationshipScale.Max;
                return Mathf.Clamp01((trustWeight * lowTrust + fatigueWeight * fatigue01) / sum);
            }
        }

        public float LostChance => Mathf.Lerp(lostChanceMin, lostChanceMax, Risk);
        public float DeathChance => Mathf.Lerp(deathChanceMin, deathChanceMax, Risk);

        public float RollLostDuration() {
            float risk = Risk;
            float min = Mathf.Lerp(lostDurationAtMinRisk.x, lostDurationAtMaxRisk.x, risk);
            float max = Mathf.Lerp(lostDurationAtMinRisk.y, lostDurationAtMaxRisk.y, risk);
            return UnityEngine.Random.Range(min, max);
        }

        public void AddTrust(float delta) {
            trust = Mathf.Clamp(trust + delta, 0f, RelationshipScale.Max);
            OnTrustChanged?.Invoke(trust);
        }

        public void AddFatigue(float delta) {
            fatigue = Mathf.Clamp(fatigue + delta, 0f, RelationshipScale.Max);
            OnFatigueChanged?.Invoke(fatigue);
        }

        public void ReportCompanionStatus(bool isAtFire, bool isLostOrDead) {
            _isCompanionAtFire = isAtFire;
            _isCompanionLostOrDead = isLostOrDead;
        }

        private void Update() {
            if (fatigueAccumulationEnabled) TickFatigue();
        }

        private void TickFatigue() {
            // Пока компаньон Lost или Dead — Fatigue не тикаем: там уже другая логика/конец истории,
            // не хотим, чтобы значение продолжало расти в фоне и путало отладку.
            if (_isCompanionLostOrDead) return;

            float delta = (_isCompanionAtFire ? -fatigueRecoveryPerSecond : fatigueGrowthPerSecond)
                          * Time.deltaTime;
            AddFatigue(delta);
        }
    }
}