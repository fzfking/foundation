using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Foundation.Logging;

namespace Foundation.Lifecycle
{
    /// <summary>
    /// The component of the first scene: it runs the application's <see cref="BootPipeline"/> and ends by loading the
    /// scene with the root scope. Used as it is, it just loads the root scene; a project derives from it to say what
    /// happens first.
    /// <list type="bullet">
    /// <item><see cref="OnBeforeBoot"/> - work that must start at once, before the first step.</item>
    /// <item><see cref="CreateSteps"/> - the steps, in order. Add the project's own (<see cref="DelegateBootStep"/> or an
    /// <see cref="IBootStep"/> class) next to the ready-made ones and end with a <see cref="SceneBootStep"/>.</item>
    /// <item><see cref="Curtain"/> - the loading curtain the bar is drawn on.</item>
    /// <item><see cref="OnBootStopped"/> - handles an intentional stop returned by a step.</item>
    /// <item><see cref="OnBootFailed"/> - what the player sees when a step throws.</item>
    /// </list>
    /// The pipeline is not tied to this component's lifetime: loading the root scene unloads this scene, and the
    /// last steps still finish.
    /// </summary>
    public class BootstrapEntryPoint : MonoBehaviour
    {
        [SerializeField] private int rootSceneIndex = 1;

        /// <summary>The build index of the scene with the root scope.</summary>
        protected int RootSceneIndex => rootSceneIndex;

        /// <summary>The loading curtain the boot draws its progress on; null when there is none.</summary>
        protected virtual ILoadingCurtain Curtain => null;

        protected virtual IAppLogger BootLogger => null;

        private void Start()
        {
            RunBootAsync().Forget();
        }

        protected virtual void OnBeforeBoot()
        {
        }

        /// <summary>Called when a boot step intentionally ends the pipeline with <see cref="BootStepOutcome.Stop"/>.</summary>
        protected virtual void OnBootStopped()
        {
        }

        protected virtual IReadOnlyList<IBootStep> CreateSteps() => new IBootStep[] { new SceneBootStep(rootSceneIndex) };

        protected virtual void OnBootFailed(Exception exception)
        {
            // The application container does not exist yet when a boot fails before it, so the Unity console is the last resort.
            var logger = BootLogger;
            if (logger != null)
                logger.Log(LogLevel.Error, "Boot", exception.ToString());
            else
                Debug.LogException(exception, this);
        }

        private async UniTaskVoid RunBootAsync()
        {
            try
            {
                OnBeforeBoot();
                if (!await new BootPipeline(CreateSteps(), Curtain, BootLogger).RunAsync())
                    OnBootStopped();
            }
            catch (Exception exception)
            {
                OnBootFailed(exception);
            }
        }
    }
}
