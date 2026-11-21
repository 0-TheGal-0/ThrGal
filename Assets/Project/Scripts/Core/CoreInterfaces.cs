using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core {
    // Пока только Lit/Dark — Fading и Regenerating добавятся на этапе 2,
    // когда появится задержка перед пересборкой контента.
    public enum ChunkState {
        Unseen,
        Lit,
        Fading,
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
    Vector3 ChunkToWorldCenter(Vector2Int coord); // новое — обратное преобразование
    ChunkState GetChunkState(Vector2Int coord);
    event Action<Vector2Int, ChunkState> OnChunkStateChanged;
}

    // Реализуется в Gameplay.asmdef на этапе 5
    public interface IRelationshipManager {
        float Trust { get; }
        float Fatigue { get; }
        event Action<float> OnTrustChanged;
        event Action<float> OnFatigueChanged;

        // Lost/Death решает CompanionController (он знает расстояние и своё состояние).
        // RelationshipManager только считает вероятности из Trust и Fatigue.
        float Risk { get; }          // 0..1
        float LostChance { get; }    // шанс Lost при отставании
        float DeathChance { get; }   // шанс Death в конце Lost
        float RollLostDuration();    // секунды, диапазон сдвигается с риском

        void AddTrust(float delta);
        void AddFatigue(float delta);

        // Push, не pull: CompanionController вызывает это каждый кадр сам.
        // Так RelationshipManager не инжектит ICompanionController — иначе получили бы
        // циклическую DI-зависимость (CompanionController ← RelationshipManager ← CompanionController).
        // isLostOrDead замораживает накопление Fatigue, пока девочка потеряна.
        void ReportCompanionStatus(bool isAtFire, bool isLostOrDead);
    }
    // Шкала Trust/Fatigue: значения лежат в [0, Max]. Тем, кому нужна доля 0..1
    // (скорость, эффекты), делить на Max.
    public static class RelationshipScale {
        public const float Max = 100f;
    }

    // Реализуется в AI.asmdef на этапе 4
    public interface ICompanionController {
        Vector3 Position { get; }
        bool IsAtFire { get; } // добавлено этап 5 — используется другими системами (UI, Sanity) без знания всего FSM
        event Action OnLost;
        event Action OnDied;
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
    // Реализуется в Procedural.asmdef на этапе 2. Список "якорных" чанков (сюжетные точки),
    // которые ChunkContentSpawner обязан пропускать при регенерации — тело девочки, ключевые
    // предметы и т.п. не должны потеряться при перегенерации мира.
        public interface IChunkAnchorRegistry {
        bool IsAnchor(Vector2Int coord);
        void RegisterAnchor(Vector2Int coord);
        void UnregisterAnchor(Vector2Int coord);
    }
    // Реализуется в Procedural.asmdef (BiomeMap). Core знает только id биома, а не его содержимое:
    // BiomeDefinition ссылается на префабы, а Core не должен знать про игровой контент.
    public interface IBiomeMap {
        // false, если в этом чанке нет биома (id 0) или карта не запечена.
        bool TryGetBiomeId(Vector2Int coord, out int biomeId);
        // Доля площади чанка под дорогой, 0..1.
        float GetRoadCoverage(Vector2Int coord);
        // true, если в этой точке мира на маске дорога.
        bool IsRoadAt(Vector3 worldPosition);
    }


}