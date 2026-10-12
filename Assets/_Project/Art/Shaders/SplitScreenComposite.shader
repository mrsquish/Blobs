// Composites up to four player views into one screen as a Voronoi split: every pixel shows the view whose players
// are nearest on screen (one site per view). Where two neighbouring cells show different views, a divider is drawn
// along their shared edge; where they show the same view (players close together) there is no seam at all.
// Driven by SplitScreenDirector.
Shader "Blobs/SplitScreenComposite"
{
    Properties
    {
        _View0 ("View 0", 2D) = "black" {}
        _View1 ("View 1", 2D) = "black" {}
        _View2 ("View 2", 2D) = "black" {}
        _View3 ("View 3", 2D) = "black" {}
        _LineColor ("Divider Colour", Color) = (0.04, 0.04, 0.07, 1)
        _LineWidth ("Divider Width (screen heights)", Float) = 0.006
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Composite"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_View0);
            TEXTURE2D(_View1);
            TEXTURE2D(_View2);
            TEXTURE2D(_View3);
            SAMPLER(sampler_linear_clamp);

            CBUFFER_START(UnityPerMaterial)
                float4 _LineColor;
                float _LineWidth;
            CBUFFER_END

            // Set every frame from script (not material properties).
            float4 _Sites[4];   // xy: on-screen centre of a view's players (viewport space), z: which view texture to show
            float _SiteCount;
            float4x4 _Seams;    // 0..1 divider strength between sites i and j (0 when they show the same view)
            float _Aspect;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 SampleView(int index, float2 uv)
            {
                if (index == 1) return SAMPLE_TEXTURE2D(_View1, sampler_linear_clamp, uv);
                if (index == 2) return SAMPLE_TEXTURE2D(_View2, sampler_linear_clamp, uv);
                if (index == 3) return SAMPLE_TEXTURE2D(_View3, sampler_linear_clamp, uv);
                return SAMPLE_TEXTURE2D(_View0, sampler_linear_clamp, uv);
            }

            float2 SitePosition(int index)
            {
                // Measure in screen-height units so cells are not stretched by the aspect ratio.
                return float2(_Sites[index].x * _Aspect, _Sites[index].y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = float2(input.uv.x * _Aspect, input.uv.y);

                int nearest = 0;
                int second = 0;
                float nearestDistance = 1e9;
                float secondDistance = 1e9;
                int count = (int)_SiteCount;
                for (int s = 0; s < 4; s++)
                {
                    if (s >= count) break;
                    float2 toSite = p - SitePosition(s);
                    float distanceSq = dot(toSite, toSite);
                    if (distanceSq < nearestDistance)
                    {
                        second = nearest;
                        secondDistance = nearestDistance;
                        nearest = s;
                        nearestDistance = distanceSq;
                    }
                    else if (distanceSq < secondDistance)
                    {
                        second = s;
                        secondDistance = distanceSq;
                    }
                }

                half4 color = SampleView((int)_Sites[nearest].z, input.uv);

                if (secondDistance < 1e8)
                {
                    // Distance from this pixel to the edge between the two nearest cells.
                    float siteGap = max(length(SitePosition(second) - SitePosition(nearest)), 1e-5);
                    float toEdge = (secondDistance - nearestDistance) / (2.0 * siteGap);
                    float strength = _Seams[nearest][second];
                    float halfWidth = _LineWidth * 0.5;
                    float onLine = 1.0 - smoothstep(halfWidth, halfWidth + 0.0015, toEdge);
                    color = lerp(color, _LineColor, onLine * strength);
                }

                return color;
            }
            ENDHLSL
        }
    }
}
