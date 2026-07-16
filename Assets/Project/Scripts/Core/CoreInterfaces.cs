using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core {
    // Пока только Lit/Dark — Fading и Regenerating добавятся на этапе 2,
    // когда появится задержка перед пересборкой контента.
    public enum ChunkState {
        Unseen,
        Lit,
        Dark
    }

    // Изменяемый объект вместо struct/tuple — это то, что позволяет менять Radius
    // в рантайме (например, при апгрейде источника света) без пересоздания регистрации.
    // ChunkManager и система тумана читают Radius каждый раз заново из этого же объекта.
    public class LightSourceHandle {
        public Transform Source;
        public float Radius;
    }

    // Общий реестр источников света. ChunkManager использует его для геймплейной логики
    // (Lit/Dark), система тумана — для рендера (карвинг видимости в шейдере). Раньше список
    // источников жил прямо в ChunkManager — вынесли, чтобы рендер не зависел от геймплейной
    // логики чанков, а геймплей — от системы тумана.
    public interface ILightRegistry {
        LightSourceHandle Register(Transform source, float radius);
        void Unregister(LightSourceHandle handle);
        IReadOnlyList<LightSourceHandle> ActiveSources { get; }
        event Action OnSourcesChanged;
    }

    // Реализуется в Procedural.asmdef на этапе 1-3
    public interface IChunkManager {
        Vector2Int WorldToChunkCoord(Vector3 worldPosition);
        ChunkState GetChunkState(Vector2Int coord);
        event Action<Vector2Int, ChunkState> OnChunkStateChanged;
    }

    // Реализуется в AI.asmdef на этапе 4
    public interface ICompanionController {
        Vector3 Position { get; }
        event Action OnLost;
        event Action OnDied;
    }

    // Реализуется в Gameplay.asmdef на этапе 5
    public interface IRelationshipManager {
        float Trust { get; }
        float Fatigue { get; }
        event Action<float> OnTrustChanged;
        event Action<float> OnFatigueChanged;
    }

    // Реализуется в Gameplay.asmdef на этапе 6
    public interface ISanityManager {
        float Sanity { get; }
        event Action<float> OnSanityChanged;
    }

    // Реализуется в Core.asmdef на этапе 7 (использует данные из остальных систем)
    public interface ISaveSystem {
        void Save();
        void Load();
    }
}