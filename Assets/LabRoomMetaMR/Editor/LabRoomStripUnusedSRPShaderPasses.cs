// This project renders with the Built-in Render Pipeline. Package shaders such as the Meta Avatars shaders also
// carry SubShaders for the Universal Render Pipeline (the URP package is installed only so those passes' #includes
// resolve - no URP asset is active). The Built-in pipeline never selects a SubShader tagged for another render
// pipeline, so this build step removes those variants before compilation: shorter builds, smaller shader data.
// It does nothing when an SRP asset is active.
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace LabRoom.MetaMR.EditorTools
{
    public class LabRoomStripUnusedSRPShaderPasses : IPreprocessShaders
    {
        static readonly ShaderTagId RenderPipelineTag = new ShaderTagId("RenderPipeline");

        public int callbackOrder => -100; // before other preprocessors spend time on variants we remove anyway

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (data.Count == 0 || GraphicsSettings.currentRenderPipeline != null) return;
            var pipeline = shader.FindSubshaderTagValue((int)snippet.pass.SubshaderIndex, RenderPipelineTag).name;
            if (string.IsNullOrEmpty(pipeline) || pipeline == "BuiltInRenderPipeline") return;
            data.Clear();
        }
    }
}
