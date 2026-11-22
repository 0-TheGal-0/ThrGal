using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Procedural {
    [Serializable]
    public class BiomeObjectEntry {
        public GameObject prefab;

        [Min(0f)] public float weight = 1f;

        [Tooltip("Редкий элемент биома. Пока это только пометка: частоту задаёт weight (мало = редко).")]
        public bool isRare;

        [Tooltip("Не ставить на склоны круче этого угла (градусы).")]
        [Range(0f, 90f)] public float maxSlopeDegrees = 35f;
        [Tooltip("Не ставить ближе этого расстояния (м) к дороге. Проверяется в точке и по 4 сторонам.")]
        [Min(0f)] public float roadClearance = 2f;

        [Tooltip("Насколько сильнее выбирать этот объект при низком Sanity (0 = не зависит). " +
                 "Пока не подключено: ISanityManager появится на этапе 6.")]
        public float lowSanityBias = 0f;

        [Tooltip("Диапазон случайного масштаба (min, max).")]
        public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
        [Tooltip("Радиус отпечатка на земле (м) при масштабе 1. В этом круге и в зазоре вокруг него другие объекты не появляются. 0 = объект считается точкой.")]
        [Min(0f)] public float footprintRadius = 0f;
    }

    // Данные одного биома. Баланс (веса, плотность) живёт здесь, а не в коде.
    [CreateAssetMenu(fileName = "Biome", menuName = "Game/Biome Definition")]
    public class BiomeDefinition : ScriptableObject {
        [Tooltip("Должен совпадать с R-каналом маски: 1 = biome1, 2 = biome2, ... 0 зарезервирован (нет биома).")]
        [SerializeField, Min(1)] private int biomeId = 1;
        [SerializeField] private string displayName = "biome";

        [Header("Плотность")]
        [Tooltip("Сколько объектов ставить в одном чанке (случайно от min до max включительно).")]
        [SerializeField, Min(0)] private int minObjectsPerChunk = 1;
        [SerializeField, Min(0)] private int maxObjectsPerChunk = 3;

        [Header("Объекты")]
        [SerializeField] private List<BiomeObjectEntry> entries = new List<BiomeObjectEntry>();

        public int BiomeId => biomeId;
        public string DisplayName => displayName;
        public int MinObjectsPerChunk => minObjectsPerChunk;
        public int MaxObjectsPerChunk => maxObjectsPerChunk;
        public IReadOnlyList<BiomeObjectEntry> Entries => entries;

        private void OnValidate() {
            if (maxObjectsPerChunk < minObjectsPerChunk) maxObjectsPerChunk = minObjectsPerChunk;
            foreach (var e in entries) {
                if (e == null) continue;
                if (e.scaleRange.y < e.scaleRange.x) e.scaleRange.y = e.scaleRange.x;
            }
        }
    }
}