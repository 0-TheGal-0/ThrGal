using UnityEngine;
using VContainer;
using Game.Core;

namespace Game.Procedural {
    // Вешается на любой объект, который должен "освещать" чанки: костёр, факел игрока,
    // факел компаньона (этап 4). Сам ничего не рендерит — просто регистрирует свою
    // позицию и радиус в ChunkManager.
    public class LightSource : MonoBehaviour {
        [SerializeField] private float radius = 8f;

        private ILightRegistry _lightRegistry;
        private LightSourceHandle _handle;

        [Inject]
        public void Construct(ILightRegistry lightRegistry) {
            _lightRegistry = lightRegistry;
        }

        // Именно Start(), не Awake() — на момент Awake() инъекция ещё может быть не выполнена
        // (см. комментарий в PlayerController.Construct про порядок вызова).
        private void Start() {
            _handle = _lightRegistry.Register(transform, radius);
        }

        private void OnDestroy() {
            if (_handle != null)
                _lightRegistry?.Unregister(_handle);
        }

        // Задел под апгрейды источника света (например, "камень души" становится ярче).
        // Меняет радиус мгновенно — ChunkManager и туман подхватят новое значение
        // со следующего кадра, без пересоздания регистрации.
        public void SetRadius(float newRadius) {
            radius = newRadius;
            if (_handle != null)
                _handle.Radius = newRadius;
        }

        private void OnDrawGizmosSelected() {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}