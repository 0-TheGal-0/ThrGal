using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Procedural {
    public class LightRegistry : MonoBehaviour, ILightRegistry {
        private readonly List<LightSourceHandle> _sources = new();

        public IReadOnlyList<LightSourceHandle> ActiveSources => _sources;
        public event Action OnSourcesChanged;

        public LightSourceHandle Register(Transform source, float radius) {
            var handle = new LightSourceHandle { Source = source, Radius = radius };
            _sources.Add(handle);
            OnSourcesChanged?.Invoke();
            return handle;
        }

        public void Unregister(LightSourceHandle handle) {
            _sources.Remove(handle);
            OnSourcesChanged?.Invoke();
        }
    }
}