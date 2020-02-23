using System;
using System.Reflection;
using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal {
    [Serializable, VolumeComponentMenu("Motion Blur from PPSv2")]
    public sealed class fMotionBlur : VolumeComponent, IPostProcessComponent {
        /// <summary>
        /// The strength of the motion blur filter. Acts as a multiplier for velocities.
        /// </summary>
        [Tooltip("The quality of the effect. Lower presets will result in better performance at the expense of visual quality.")]
        public ClampedFloatParameter shutterAngle = new ClampedFloatParameter(0f, 0f, 360f);

        /// <summary>
        /// The quality of the effect.
        /// </summary>
        [Tooltip("The strength of the motion blur filter. Acts as a multiplier for velocities.")]
        public ClampedIntParameter sampleCount = new ClampedIntParameter(10, 4, 32);

        /// <summary>
        /// Is the component active?
        /// </summary>
        /// <returns>True is the component is active</returns>
        public bool IsActive() => shutterAngle.value > 0f;

        /// <summary>
        /// Is the component compatible with on tile rendering
        /// </summary>
        /// <returns>false</returns>
        public bool IsTileCompatible() => false;

#if UNITY_EDITOR
		protected override void OnEnable() {
			base.OnEnable();

			var forward = UniversalRenderPipeline.asset.scriptableRenderer as ForwardRenderer;
			if (forward != null) {
				var prop = typeof(ScriptableRenderer).GetProperty("rendererFeatures", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetProperty);
				var features = prop.GetValue(forward) as List<ScriptableRendererFeature>;
				bool missing = true;
				foreach (var f in features) {
					if (f is fMotionFeature) {
						missing = false;
						break;
					}
				}

				// ScriptableRendererFeature is Missing when you reimport the project...
				if (missing)
					Debug.LogWarning("Missing fMotion Feature...");
			}
		}
#endif
	}
}
