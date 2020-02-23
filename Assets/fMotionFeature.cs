using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class fMotionFeature : ScriptableRendererFeature {
    #region DEFINE
    [System.Serializable]
    public class Settings {
        public MotionVectorSettings motionVector = new MotionVectorSettings();
        public MotionBlurSettings motionBlur = new MotionBlurSettings();
    }

    [System.Serializable]
    public class MotionVectorSettings {
        public RenderPassEvent Event = RenderPassEvent.AfterRenderingSkybox;
        public LayerMask LayerMask = 0;
        public Shader shader = null;
    }
    [System.Serializable]
    public class MotionBlurSettings {
        public RenderPassEvent Event = RenderPassEvent.BeforeRenderingPostProcessing;
        public Shader shader = null;
    }
    #endregion

    public Settings settings = new Settings();

    private MotionVectorPass motionVectorPass;
    private MotionBlurPass motionBlurPass;


    #region Motion Vector
	/// <summary>
	/// Motion Vector Process
	/// </summary>
    class MotionVectorPass : ScriptableRenderPass {
        private const string PASS_NAME = "fMotionVector";
        private const string PRE_PASS_NAME = "Copy Depth";
        private ProfilingSampler profilingSampler = new ProfilingSampler(PASS_NAME);
        private ShaderTagId SHADER_TAG_FORWARD = new ShaderTagId("UniversalForward");
        private static readonly int MOTION_TEXTURE = Shader.PropertyToID("_CameraMotionVectorsTexture");
        private static readonly int PROP_VPMATRIX = Shader.PropertyToID("_NonJitteredVP");
        private static readonly int PROP_PREV_VPMATRIX = Shader.PropertyToID("_PreviousVP");

        private MotionVectorSettings settings = null;
        private Material blitMaterial;
        private Matrix4x4 previousVP = Matrix4x4.identity;


        public MotionVectorPass(MotionVectorSettings settings) {
            this.settings = settings;
            this.renderPassEvent = settings.Event;

            this.blitMaterial = CoreUtils.CreateEngineMaterial(settings.shader);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
#if UNITY_EDITOR || DEBUG
            if (renderingData.cameraData.isSceneViewCamera || !Application.isPlaying)
                return;
            if (renderingData.cameraData.camera.cameraType == CameraType.Preview)
                return;
            if (this.blitMaterial == null)
                return;
#endif
            var volumeComponent = VolumeManager.instance.stack.GetComponent<fMotionBlur>();
            if (!volumeComponent.IsActive())
                return;

            CommandBuffer cmd = CommandBufferPool.Get(PASS_NAME);

            using (new ProfilingScope(cmd, profilingSampler)) {
#if UNITY_EDITOR || DEBUG
                // for FrameDebugger
                context.ExecuteCommandBuffer(cmd);
#endif
                cmd.Clear();

                var camera = renderingData.cameraData.camera;
                // NOTE: UnityEngine require this flags to update unity_PreviousM.
                camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;

#if UNITY_EDITOR || DEBUG
				// for FrameDebugger
                CommandBuffer preCmd = CommandBufferPool.Get(PRE_PASS_NAME);
                preCmd.Clear();
#else
                var preCmd = cmd;
#endif
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                // Create MotionVectorTexture
				// NOTE : Depth Buffer is faster 32bit(24bit) than 16bit. https://gpuopen.com/dcc-overview/
                preCmd.GetTemporaryRT(MOTION_TEXTURE, descriptor.width, descriptor.height, 32, FilterMode.Point, RenderTextureFormat.RGHalf);
                this.Blit(preCmd, BuiltinRenderTextureType.None, MOTION_TEXTURE, this.blitMaterial, 1);
                context.ExecuteCommandBuffer(preCmd);
#if UNITY_EDITOR || DEBUG
				// for FrameDebugger
                CommandBufferPool.Release(preCmd);
#endif

                // Camara Motion
                //var proj = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, true); // if you want to use previousViewProjectionMatrix
				var proj = camera.nonJitteredProjectionMatrix;
                var view = camera.worldToCameraMatrix;
                var viewProj = proj * view;
                this.blitMaterial.SetMatrix(PROP_VPMATRIX, viewProj);
                // NOTE: camera.previousViewProjectionMatrix doesn't be updated when camera don't move.
                this.blitMaterial.SetMatrix(PROP_PREV_VPMATRIX, this.previousVP);
                this.previousVP = viewProj;

                var drawingSettings = this.CreateDrawingSettings(SHADER_TAG_FORWARD, ref renderingData, SortingCriteria.CommonOpaque);
                drawingSettings.overrideMaterial = this.blitMaterial;
                drawingSettings.overrideMaterialPassIndex = 0;
                drawingSettings.perObjectData |= PerObjectData.MotionVectors; // MotionVector—LŒø‰»
                var filteringSettings = new FilteringSettings(RenderQueueRange.opaque, this.settings.LayerMask);
                var renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);
            }
			
#if UNITY_EDITOR || DEBUG
            // for FrameDebugger
            context.ExecuteCommandBuffer(cmd);
#endif
            CommandBufferPool.Release(cmd);
        }
    }
    #endregion


    #region Motion Blur
    /// <summary>
    /// Motion Blur Process
    /// NOTE: compatible MotionBlur(PPSv2)
    /// NOTE: You should do it with PostProcessPass, but you need to prepare your own ScriptableRenderer...
	///       So this is made as a ScriptableRendererFeature.
    /// </summary>
    class MotionBlurPass : ScriptableRenderPass {
        private const string PASS_NAME = "fMotionBlur";
        private ProfilingSampler profilingSampler = new ProfilingSampler(PASS_NAME);
        private Material blitMaterial;
        //private Material copyMaterial;
        private Mesh triangle;
        private bool isSupportedFloatBuffer = false;
        private bool resetHistory = false;

        public MotionBlurPass(MotionBlurSettings settings) {
            this.renderPassEvent = settings.Event;
            this.isSupportedFloatBuffer = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB2101010);

            this.blitMaterial = CoreUtils.CreateEngineMaterial(settings.shader);
            this.resetHistory = true;
            this.triangle = new Mesh();
            this.triangle.name = "Fullscreen Triangle";
            // Because we have to support older platforms (GLES2/3, DX9 etc) we can't do all of
            // this directly in the vertex shader using vertex ids :(
            this.triangle.SetVertices(new[] {
                    new Vector3(-1f, -1f, 0f),
                    new Vector3(-1f,  3f, 0f),
                    new Vector3( 3f, -1f, 0f)
            });
            this.triangle.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false);
            this.triangle.UploadMeshData(true);
        }

        private enum Pass {
            VelocitySetup,
            TileMax1,
            TileMax2,
            TileMaxV,
            NeighborMax,
            Reconstruction
        }

        private static readonly int MAIN_TEX = Shader.PropertyToID("_MainTex");
        private static readonly int TEMP_COLOR_TEXTURE = Shader.PropertyToID("Temp Color Buffer");
        private static readonly int MOTION_TEXTURE = Shader.PropertyToID("_CameraMotionVectorsTexture");
        private static readonly int VelocityScale = Shader.PropertyToID("_VelocityScale");
        private static readonly int MaxBlurRadius = Shader.PropertyToID("_MaxBlurRadius");
        private static readonly int RcpMaxBlurRadius = Shader.PropertyToID("_RcpMaxBlurRadius");
        private static readonly int VelocityTex = Shader.PropertyToID("_VelocityTex");
        private static readonly int Tile2RT = Shader.PropertyToID("_Tile2RT");
        private static readonly int Tile4RT = Shader.PropertyToID("_Tile4RT");
        private static readonly int Tile8RT = Shader.PropertyToID("_Tile8RT");
        private static readonly int TileMaxOffs = Shader.PropertyToID("_TileMaxOffs");
        private static readonly int TileMaxLoop = Shader.PropertyToID("_TileMaxLoop");
        private static readonly int TileVRT = Shader.PropertyToID("_TileVRT");
        private static readonly int NeighborMaxTex = Shader.PropertyToID("_NeighborMaxTex");
        private static readonly int LoopCount = Shader.PropertyToID("_LoopCount");

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
#if UNITY_EDITOR || DEBUG
            if (renderingData.cameraData.isSceneViewCamera || !Application.isPlaying)
                return;
            if (renderingData.cameraData.camera.cameraType == CameraType.Preview)
                return;
            if (this.blitMaterial == null)
                return;
            //if (this.copyMaterial == null)
            //    return;
#endif
            var volumeComponent = VolumeManager.instance.stack.GetComponent<fMotionBlur>();
            if (this.resetHistory) {
                this.resetHistory = false;
                return;
            }
			if (!volumeComponent.IsActive()) {
				this.resetHistory = true;
				return;
			}

            CommandBuffer cmd = CommandBufferPool.Get(PASS_NAME);

            using (new ProfilingScope(cmd, profilingSampler)) {
                cmd.Clear();

                //cmd.SetGlobalFloat("_RenderViewportScaleFactor", 1.0f); // for XR Parameter

                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                int width = descriptor.width;
                int height = descriptor.height;

                cmd.GetTemporaryRT(TEMP_COLOR_TEXTURE, width, height, 0, FilterMode.Bilinear, descriptor.colorFormat);
                cmd.CopyTexture(this.colorAttachment, new RenderTargetIdentifier(TEMP_COLOR_TEXTURE));
                //cmd.SetGlobalTexture("_MainTex", this.colorAttachment);
                //this.Blit(cmd, this.colorAttachment, TEMP_COLOR_TEXTURE, this.copyMaterial, 1);

                float shutterAngle = volumeComponent.shutterAngle.value;
                int sampleCount = volumeComponent.sampleCount.value;

				//------------------------------------------------------------
				// compatible PPSv2

                const float kMaxBlurRadius = 5f;
                var vectorRTFormat = RenderTextureFormat.RGHalf;
                var packedRTFormat = this.isSupportedFloatBuffer
                    ? RenderTextureFormat.ARGB2101010
                    : RenderTextureFormat.ARGB32;

                // Calculate the maximum blur radius in pixels.
                int maxBlurPixels = (int)(kMaxBlurRadius * height / 100);

                // Calculate the TileMax size.
                // It should be a multiple of 8 and larger than maxBlur.
                int tileSize = ((maxBlurPixels - 1) / 8 + 1) * 8;

                // Pass 1 - Velocity/depth packing
                var velocityScale = shutterAngle / 360f;
                this.blitMaterial.SetFloat(VelocityScale, velocityScale);
                this.blitMaterial.SetFloat(MaxBlurRadius, maxBlurPixels);
                this.blitMaterial.SetFloat(RcpMaxBlurRadius, 1f / maxBlurPixels);

                //int vbuffer = ShaderIDs.VelocityTex;
                int vbuffer = VelocityTex;
                cmd.GetTemporaryRT(vbuffer, width, height, 0, FilterMode.Point,
                    packedRTFormat, RenderTextureReadWrite.Linear);
                this.BlitFullscreenTriangle(cmd, BuiltinRenderTextureType.None, vbuffer, (int)Pass.VelocitySetup);

                // Pass 2 - First TileMax filter (1/2 downsize)
                //int tile2 = ShaderIDs.Tile2RT;
                int tile2 = Tile2RT;
                cmd.GetTemporaryRT(tile2, width / 2, height / 2, 0, FilterMode.Point,
                    vectorRTFormat, RenderTextureReadWrite.Linear);
                this.BlitFullscreenTriangle(cmd, vbuffer, tile2, (int)Pass.TileMax1);

                // Pass 3 - Second TileMax filter (1/2 downsize)
                //int tile4 = ShaderIDs.Tile4RT;
                int tile4 = Tile4RT;
                cmd.GetTemporaryRT(tile4, width / 4, height / 4, 0, FilterMode.Point,
                    vectorRTFormat, RenderTextureReadWrite.Linear);
                this.BlitFullscreenTriangle(cmd, tile2, tile4, (int)Pass.TileMax2);
                cmd.ReleaseTemporaryRT(tile2);

                // Pass 4 - Third TileMax filter (1/2 downsize)
                //int tile8 = ShaderIDs.Tile8RT;
                int tile8 = Tile8RT;
                cmd.GetTemporaryRT(tile8, width / 8, height / 8, 0, FilterMode.Point,
                    vectorRTFormat, RenderTextureReadWrite.Linear);
                this.BlitFullscreenTriangle(cmd, tile4, tile8, (int)Pass.TileMax2);
                cmd.ReleaseTemporaryRT(tile4);

                // Pass 5 - Fourth TileMax filter (reduce to tileSize)
                var tileMaxOffs = Vector2.one * (tileSize / 8f - 1f) * -0.5f;
                this.blitMaterial.SetVector(TileMaxOffs, tileMaxOffs);
                this.blitMaterial.SetFloat(TileMaxLoop, (int)(tileSize / 8f));

                //int tile = ShaderIDs.TileVRT;
                int tile = TileVRT;
                cmd.GetTemporaryRT(tile, width / tileSize, height / tileSize, 0,
                    FilterMode.Point, vectorRTFormat, RenderTextureReadWrite.Linear);
                this.BlitFullscreenTriangle(cmd, tile8, tile, (int)Pass.TileMaxV);
                cmd.ReleaseTemporaryRT(tile8);

                // Pass 6 - NeighborMax filter
                int neighborMax = NeighborMaxTex;
                int neighborMaxWidth = width / tileSize;
                int neighborMaxHeight = height / tileSize;
                cmd.GetTemporaryRT(neighborMax, neighborMaxWidth, neighborMaxHeight, 0,
                    FilterMode.Point, vectorRTFormat, RenderTextureReadWrite.Linear);
                this.BlitFullscreenTriangle(cmd, tile, neighborMax, (int)Pass.NeighborMax);
                cmd.ReleaseTemporaryRT(tile);

                // Pass 7 - Reconstruction pass
                this.blitMaterial.SetFloat(LoopCount, Mathf.Clamp(sampleCount / 2, 1, 64));
                this.BlitFullscreenTriangle(cmd, TEMP_COLOR_TEXTURE, this.colorAttachment, (int)Pass.Reconstruction);

                cmd.ReleaseTemporaryRT(vbuffer);
                cmd.ReleaseTemporaryRT(neighborMax);

                cmd.ReleaseTemporaryRT(TEMP_COLOR_TEXTURE);
				//------------------------------------------------------------

                cmd.ReleaseTemporaryRT(MOTION_TEXTURE); // Release MotionVectorTexture created in MotionVectorPass
                cmd.SetRenderTarget(this.colorAttachment, this.depthAttachment);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void BlitFullscreenTriangle(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, int pass/*, bool clear = false*/) {
            cmd.SetGlobalTexture(MAIN_TEX, source);
            //RenderBufferLoadAction loadAction = clear ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare;
			RenderBufferLoadAction loadAction = RenderBufferLoadAction.DontCare;
            cmd.SetRenderTarget(destination, loadAction, RenderBufferStoreAction.Store);

            //if (clear)
            //    cmd.ClearRenderTarget(true, true, Color.clear);

            cmd.DrawMesh(this.triangle, Matrix4x4.identity, this.blitMaterial, 0, pass);
        }
    }
    #endregion


    public override void Create() {
#if UNITY_EDITOR
        if (this.settings.motionVector.shader == null) {
            this.settings.motionVector.shader = Shader.Find("Hidden/MotionVectors");
            this.settings.motionVector.LayerMask = LayerMask.NameToLayer("Everything");
        }
        if (this.settings.motionBlur.shader == null)
            this.settings.motionBlur.shader = Shader.Find("Hidden/PostProcessing/MotionBlur");

        SupportedRenderingFeatures.active.motionVectors = true;				// NOTE: Enable MotionVector setting for Renderer on Inspector
        UniversalRenderPipeline.asset.supportsCameraDepthTexture = true;	// NOTE: Require DepthTexture
#endif

        this.motionVectorPass = new MotionVectorPass(this.settings.motionVector);
        this.motionBlurPass = new MotionBlurPass(this.settings.motionBlur);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        this.motionVectorPass.ConfigureTarget(renderer.cameraColorTarget, renderer.cameraDepth);
        renderer.EnqueuePass(this.motionVectorPass);
        this.motionBlurPass.ConfigureTarget(renderer.cameraColorTarget, renderer.cameraDepth);
        renderer.EnqueuePass(this.motionBlurPass);
    }
}

