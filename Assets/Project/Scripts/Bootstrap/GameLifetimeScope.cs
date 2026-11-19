using Game.Core;
using Game.Procedural;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using Game.AI;
using Game.Gameplay;

namespace Game.Bootstrap {
    // Вешается на единственный GameObject в корневой сцене (например "Root" / "Composition Root").
    // Сцена должна быть загружена первой (Additive-сцена или первая в Build Settings).
    public class GameLifetimeScope : LifetimeScope {
        [SerializeField] private int worldSeed = 12345; 
        protected override void Configure(IContainerBuilder builder) {
            // GameManager — единственная реализация на этапе 0.
            builder.Register<GameManager>(Lifetime.Singleton);

            // Дальше системы подключаются по мере готовности, каждая — своей строкой:
            //
            // builder.Register<ICompanionController, CompanionController>(Lifetime.Singleton); // этап 4
            // builder.Register<IRelationshipManager, RelationshipManager>(Lifetime.Singleton);  // этап 5
            // builder.Register<ISanityManager, SanityManager>(Lifetime.Singleton);      // этап 6
            // builder.Register<ISaveSystem, SaveSystem>(Lifetime.Singleton);            // этап 7
            //
            // Реализации живут в своих asmdef (Procedural, AI, Gameplay) и НЕ известны Core
            // на этапе компиляции — сюда их подключает только это Configure(), которое
            // технически может ссылаться на все asmdef (LifetimeScope — единственное
            // место в проекте, где нарушение "Core не знает про остальных" оправдано,
            // это и есть работа composition root).

            // MonoBehaviour-компоненты сцены нужно явно зарегистрировать, чтобы VContainer
            // их нашёл и вызвал их [Inject]-метод. RegisterComponentInHierarchy ищет
            // компонент на сценe автоматически — не нужно вручную тащить ссылку через инспектор.
            builder.RegisterComponentInHierarchy<PlayerController>();
            builder.RegisterComponentInHierarchy<CompanionController>().As<ICompanionController>();
            builder.RegisterComponentInHierarchy<RelationshipManager>().As<IRelationshipManager>();
            builder.RegisterComponentInHierarchy<PlayerFatigueFeedback>();



            // Этап 1: ChunkManager регистрируется и как конкретный тип, и как IChunkManager —
            // второе нужно, чтобы LightSource (и любой другой скрипт) мог запросить его через
            // интерфейс, не зная про Procedural.asmdef напрямую.
            // Этап 1: LightRegistry — общий реестр источников света, используется и
            // ChunkManager (геймплей), и системой тумана (рендер), но сам ничего
            // не знает ни про то, ни про другое.
            builder.RegisterComponentInHierarchy<LightRegistry>().As<ILightRegistry>();

            builder.RegisterComponentInHierarchy<ChunkManager>().As<IChunkManager>();

            // Слой 2 тумана: публикует данные ILightRegistry в шейдеры каждый кадр.
            builder.RegisterComponentInHierarchy<LightFogFeeder>();
            // Этап 2: слушает Dark-переходы от ChunkManager и подменяет placeholder-контент чанка.
            builder.RegisterComponentInHierarchy<ChunkContentSpawner>();

            // ВАЖНО: RegisterComponentInHierarchy находит и инжектит только ОДИН экземпляр
            // компонента в сцене (внутри — единственный Lazy<T>). У нас несколько LightSource
            // (костёр + факел игрока, дальше ещё факел компаньона на этапе 4), поэтому вместо
            // RegisterComponentInHierarchy используем RegisterBuildCallback: он выполняется
            // один раз сразу после сборки контейнера и вручную инжектит КАЖДЫЙ найденный
            // в сцене LightSource через resolver.Inject(...).
            builder.RegisterBuildCallback(resolver =>   {
                var lightSources = UnityEngine.Object.FindObjectsByType<LightSource>(
                    FindObjectsSortMode.None);
                foreach (var lightSource in lightSources) {
                    resolver.Inject(lightSource);
                }
            });
            builder.RegisterBuildCallback(resolver => {
                var anchors = UnityEngine.Object.FindObjectsByType<NarrativeAnchor>(FindObjectsSortMode.None);
                foreach (var anchor in anchors) {
                    resolver.Inject(anchor);
                }
            });
            builder.Register<ChunkSeedProvider>(Lifetime.Singleton)
    .WithParameter("globalSeed", worldSeed);

            builder.RegisterComponentInHierarchy<ChunkAnchorRegistry>().As<IChunkAnchorRegistry>();
            builder.RegisterComponentInHierarchy<BiomeMap>().AsSelf().As<IBiomeMap>();
        }
    }
}   