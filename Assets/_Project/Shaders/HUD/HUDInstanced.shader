// ============================================================================
// HUDInstanced.shader
// GPU 驱动的 HUD 渲染 Shader
// 单个 DrawCall 绘制所有 HUD 元素：头像、图标、血条、SDF文字、飘血
// 使用 DrawMeshInstancedIndirect + StructuredBuffer
// ============================================================================

Shader "HUD/GPUInstanced"
{
    Properties
    {
        _MainAtlas ("主 Atlas（图标/SDF字体/血条）", 2D) = "white" {}
        [NoScaleOffset] _AvatarArray ("头像 Texture2DArray", 2DArray) = "" {}
        _SDFThreshold ("SDF 阈值", Range(0, 1)) = 0.5
        _SDFSoftness ("SDF 柔化宽度", Range(0, 0.2)) = 0.05
        _FloatRiseHeight ("飘血上浮高度", Float) = 0.05
        _FloatBounceScale ("飘血弹跳幅度", Float) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "HUD_GPU_INSTANCED"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex HUDVert
            #pragma fragment HUDFrag
            #pragma target 4.5
            #pragma exclude_renderers gles gles3 glcore

            #include "HUDCommon.hlsl"

            // ================================================================
            // Vertex Shader
            // 世界坐标 → 屏幕空间 Billboard，GPU 视锥剔除
            // ================================================================
            Varyings HUDVert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings o = (Varyings)0;

                // 读取 Instance 数据
                HUDInstanceData data = _HUDBuffer[instanceID];

                // 1. 世界坐标 → 裁剪空间
                float4 clipPos = mul(UNITY_MATRIX_VP, float4(data.worldPosition, 1.0));

                // 2. GPU 视锥剔除
                float cullMask = FrustumCull(clipPos);
                uint visible = GetVisible(data.flags);
                cullMask *= (float)visible;

                // 3. 透视除法 → NDC
                float2 ndcPos = clipPos.xy / clipPos.w;

                // 4. 像素偏移（屏幕空间）
                float2 pixelToNDC = float2(2.0 / _ScreenParams.x, 2.0 / _ScreenParams.y);
                float2 offset = float2(data.screenOffsetX, data.screenOffsetY) * pixelToNDC;
                ndcPos += offset;

                // 5. Quad 顶点展开（input.positionOS.xy 范围 [-0.5, 0.5]）
                float2 quadSize = data.size * pixelToNDC;
                float2 centerNDC = ndcPos;
                ndcPos += input.positionOS.xy * quadSize * cullMask;

                // 6. 飘血动画
                float animTime = 0.0;
                uint elemType = GetElementType(data.flags);
                if (elemType == ELEM_TYPE_FLOATTEXT)
                {
                    // 计算动画进度
                    float elapsed = _Time.y - data.color.a; // 复用 color.a 存储 startTime
                    float duration = _FloatRiseHeight > 0 ? 0.8 : 1.0; // 固定 0.8s
                    float t = saturate(elapsed / 0.8);
                    animTime = t;

                    // 上浮
                    ndcPos.y += t * _FloatRiseHeight;

                    // 弹跳缩放
                    float bounce = BounceScale(t, _FloatBounceScale);
                    ndcPos = lerp(centerNDC + offset, ndcPos, bounce);
                }

                // 7. 组装裁剪空间坐标
                o.positionCS = float4(ndcPos * clipPos.w, clipPos.z, clipPos.w);

                // 8. UV 映射：从 Atlas 中的子区域采样
                o.uv = input.uv * data.uvRect.zw + data.uvRect.xy;

                // 9. 传递颜色和标记
                o.color = data.color;
                o.flags = data.flags;
                o.localU = input.uv.x;
                o.animTime = animTime;

                return o;
            }

            // ================================================================
            // Fragment Shader
            // 按元素类型分支采样不同纹理区域
            // ================================================================
            float4 HUDFrag(Varyings input) : SV_Target
            {
                uint elemType = GetElementType(input.flags);
                float4 col = float4(0, 0, 0, 0);

                // === Avatar（头像）===
                if (elemType == ELEM_TYPE_AVATAR)
                {
                    uint sliceIndex = GetAvatarSlice(input.flags);
                    col = SAMPLE_TEXTURE2D_ARRAY(_AvatarArray, sampler_AvatarArray,
                                                  input.uv, sliceIndex);

                    // 圆形裁剪
                    float2 center = input.uv - 0.5;
                    float distSq = dot(center, center);
                    // 使用 smoothstep 实现抗锯齿圆形边缘
                    col.a *= 1.0 - smoothstep(0.23, 0.25, distSq);
                }
                // === Text / FloatText（SDF 文字）===
                else if (elemType == ELEM_TYPE_TEXT || elemType == ELEM_TYPE_FLOATTEXT)
                {
                    // SDF 采样
                    float dist = SAMPLE_TEXTURE2D(_MainAtlas, sampler_MainAtlas, input.uv).a;
                    float alpha = smoothstep(_SDFThreshold - _SDFSoftness,
                                             _SDFThreshold + _SDFSoftness, dist);
                    col = float4(input.color.rgb, alpha);

                    // 飘血淡出
                    if (elemType == ELEM_TYPE_FLOATTEXT)
                    {
                        col.a *= FadeOut(input.animTime);
                    }
                }
                // === HealthBar（血条）===
                else if (elemType == ELEM_TYPE_HEALTHBAR)
                {
                    col = SAMPLE_TEXTURE2D(_MainAtlas, sampler_MainAtlas, input.uv);
                    col *= input.color;

                    // 血条前景裁剪：color.a 编码了血量百分比
                    // localU > healthPercent 的像素被裁剪
                    col.a *= step(input.localU, input.color.a);
                }
                // === Icon（图标）===
                else
                {
                    col = SAMPLE_TEXTURE2D(_MainAtlas, sampler_MainAtlas, input.uv);
                    col *= input.color;
                }

                return col;
            }

            ENDHLSL
        }
    }

    FallBack Off
}
