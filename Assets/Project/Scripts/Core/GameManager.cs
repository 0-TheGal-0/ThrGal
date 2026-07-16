using System;
using UnityEngine;
using VContainer;

namespace Game.Core
{
    public enum GamePhase
    {
        Loading,
        Playing,
        Paused,
        Ending
    }

    // Никакой игровой логики систем здесь — только глобальное состояние и события уровня "игра целиком".
    // Конкретные системы (свет, компаньон, отношения) сами подписываются на нужные события через DI.
    public class GameManager
    {
        public GamePhase Phase { get; private set; } = GamePhase.Loading;

        public event Action<GamePhase> OnPhaseChanged;

        // Пустой конструктор на этапе 0. Когда появятся IRelationshipManager/ISanityManager
        // (этапы 5-6) — просто добавьте их параметрами сюда, VContainer подставит
        // зарегистрированные реализации автоматически, ничего в LifetimeScope менять не придётся.
        [Inject]
        public GameManager()
        {
        }

        public void SetPhase(GamePhase newPhase)
        {
            if (Phase == newPhase) return;
            Phase = newPhase;
            OnPhaseChanged?.Invoke(newPhase);

            Time.timeScale = newPhase == GamePhase.Paused ? 0f : 1f;
        }
    }
}
