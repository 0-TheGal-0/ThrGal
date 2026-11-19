using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer;
using Game.Core;

namespace Game.Gameplay {
    // Отдельный маленький компонент, а не логика внутри PlayerController — чтобы визуальный
    // фидбек можно было включать/выключать/тюнить независимо от движения игрока.
    // Требует Volume с Vignette-оверрайдом в профиле (см. инструкцию по настройке ниже).
    public class PlayerFatigueFeedback : MonoBehaviour {
        [SerializeField] private Volume postProcessVolume;
        [SerializeField] private float minVignetteIntensity = 0.15f;
        [SerializeField] private float maxVignetteIntensity = 0.55f;

        //private IRelationshipManager _relationshipManager;
        //private Vignette _vignette;

        //[Inject]
        //public void Construct(IRelationshipManager relationshipManager) {
        //    _relationshipManager = relationshipManager;
        //}

        //private void Start() {
        //    if (postProcessVolume == null || postProcessVolume.profile == null) {
        //        Debug.LogWarning("PlayerFatigueFeedback: Volume/Profile не назначен, визуальный дебафф не будет работать.");
        //        return;
        //    }
        //    postProcessVolume.profile.TryGet(out _vignette);
        //    ApplyFatigue(_relationshipManager.Fatigue);
        //}

        //private void OnEnable() {
        //    _relationshipManager.OnFatigueChanged += ApplyFatigue;
        //}

        //private void OnDisable() {
        //    _relationshipManager.OnFatigueChanged -= ApplyFatigue;
        //}

        //private void ApplyFatigue(float fatigue) {
        //    if (_vignette == null) return;
        //    _vignette.intensity.Override(Mathf.Lerp(minVignetteIntensity, maxVignetteIntensity, fatigue));
        //}
    }
}