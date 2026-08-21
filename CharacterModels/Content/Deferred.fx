// Deferred lighting passes: shadowed directional + hemisphere ambient (full-screen), sphere-volume point lights
// (additive), and the composite (albedo * light + spec + rim + emissive, fog, tone map).
#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_3
    #define PS_SHADERMODEL ps_4_0_level_9_3
#endif

float4x4 WorldViewProjection;   // point-light sphere volume
float4x4 InvViewProjection;
float4x4 LightViewProjection;
float3 CameraPosition;

// directional
float3 LightDirection;
float3 LightColor;
float3 FillDirection;
float3 FillColor;
float3 SkyColor;
float3 GroundColor;
float  ShadowMapSize;
float  ShadowStrength;

// point light
float3 PointPosition;
float3 PointColor;
float  PointRadius;
float  PointIntensity;
float  UvFlip;          // +1: DirectX convention, -1: OpenGL render-target y flip

// composite
float3 RimColor;
float  FogStart;
float  FogEnd;
float3 FogColor;

texture AlbedoTex; sampler AlbedoSampler = sampler_state { Texture = <AlbedoTex>; };
texture NormalTex; sampler NormalSampler = sampler_state { Texture = <NormalTex>; };
texture DepthTex;  sampler DepthSampler  = sampler_state { Texture = <DepthTex>; };
texture LightTex;  sampler LightSampler  = sampler_state { Texture = <LightTex>; };
texture ShadowMap; sampler ShadowSampler = sampler_state { Texture = <ShadowMap>; };

struct QuadVSIn  { float3 Position : POSITION0; float2 TexCoord : TEXCOORD0; };
struct QuadVSOut { float4 Position : SV_POSITION; float2 TexCoord : TEXCOORD0; };

QuadVSOut QuadVS(QuadVSIn input)
{
    QuadVSOut o;
    o.Position = float4(input.Position, 1);
    o.TexCoord = input.TexCoord;
    return o;
}

// ---- helpers ---------------------------------------------------------------
float3 ReconstructWorld(float2 uv, float depth)
{
    float4 ndc = float4(uv.x * 2 - 1, (1 - uv.y) * 2 - 1, depth, 1);
    float4 p = mul(ndc, InvViewProjection);
    return p.xyz / p.w;
}

float SampleShadow(float3 worldPos, float ndotl)
{
    float4 lp = mul(float4(worldPos, 1), LightViewProjection);
    float3 proj = lp.xyz / lp.w;
    float2 uv = float2(proj.x * 0.5 + 0.5, 0.5 - proj.y * 0.5);
    if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return 1.0;
    float bias = max(0.0015, 0.006 * (1.0 - ndotl));
    float texel = 1.0 / ShadowMapSize;
    float lit = 0;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
        lit += (proj.z - bias <= tex2D(ShadowSampler, uv + float2(x, y) * texel).r) ? 1.0 : 0.0;
    return lit / 9.0;
}

// Shared shading for one light: returns (diffuse rgb, spec) given N, L, V, material.
float4 Shade(float3 N, float3 L, float3 V, float3 color, float specStrength, float shininess, float wrap)
{
    float ndotl = dot(N, L);
    float diff = saturate((ndotl + wrap) / (1.0 + wrap));
    float3 H = normalize(L + V);
    float sh = lerp(8.0, 160.0, shininess);
    float spec = pow(saturate(dot(N, H)), sh) * specStrength * saturate(ndotl * 4.0);
    spec *= 1.0 + 2.0 * pow(1.0 - saturate(dot(N, V)), 3.0) * specStrength;
    return float4(color * diff, spec * dot(color, float3(0.299, 0.587, 0.114)));
}

// ---- directional + ambient (full screen) ------------------------------------
float4 DirectionalPS(QuadVSOut input) : COLOR0
{
    float4 nrm = tex2D(NormalSampler, input.TexCoord);
    float4 alb = tex2D(AlbedoSampler, input.TexCoord);
    float depth = tex2D(DepthSampler, input.TexCoord).r;
    if (depth >= 0.99999) return float4(0, 0, 0, 0);            // nothing was written here
    float3 N = normalize(nrm.xyz * 2 - 1);
    float3 P = ReconstructWorld(input.TexCoord, depth);
    float3 V = normalize(CameraPosition - P);
    float3 L = -normalize(LightDirection);

    float shadow = lerp(1.0, SampleShadow(P, dot(N, L)), ShadowStrength);
    float4 key = Shade(N, L, V, LightColor, alb.a, nrm.a, 0.15);
    float fillD = saturate(dot(N, -normalize(FillDirection)) * 0.5 + 0.5);
    float3 ambient = lerp(GroundColor, SkyColor, N.y * 0.5 + 0.5) + FillColor * fillD;
    return float4(key.rgb * shadow + ambient, key.a * shadow);
}

// ---- point light (sphere volume) ----------------------------------------------
struct VolVSIn  { float3 Position : POSITION0; };
struct VolVSOut { float4 Position : SV_POSITION; float4 ClipPos : TEXCOORD0; };

VolVSOut VolumeVS(VolVSIn input)
{
    VolVSOut o;
    o.Position = mul(float4(input.Position, 1), WorldViewProjection);
    o.ClipPos = o.Position;
    return o;
}

float4 PointPS(VolVSOut input) : COLOR0
{
    float2 ndc = input.ClipPos.xy / input.ClipPos.w;
    float2 uv = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5 * UvFlip);
    float depth = tex2D(DepthSampler, uv).r;
    if (depth >= 0.99999) return float4(0, 0, 0, 0);
    float4 nrm = tex2D(NormalSampler, uv);
    float4 alb = tex2D(AlbedoSampler, uv);
    float3 N = normalize(nrm.xyz * 2 - 1);
    float3 P = ReconstructWorld(uv, depth);
    float3 toL = PointPosition - P;
    float d = length(toL);
    float x = saturate(1.0 - d * d / (PointRadius * PointRadius));
    float atten = x * x * PointIntensity;                      // exactly 0 at the radius: volume edge is invisible
    float3 L = toL / max(d, 1e-4);
    float3 V = normalize(CameraPosition - P);
    float4 s = Shade(N, L, V, PointColor, alb.a, nrm.a, 0.0);
    return s * atten;
}

// ---- composite -------------------------------------------------------------------
float4 CompositePS(QuadVSOut input) : COLOR0
{
    float4 alb = tex2D(AlbedoSampler, input.TexCoord);
    float4 nrm = tex2D(NormalSampler, input.TexCoord);
    float4 light = tex2D(LightSampler, input.TexCoord);
    float depth = tex2D(DepthSampler, input.TexCoord).r;
    float3 color;
    float3 albedo = pow(alb.rgb, 2.2);
    if (depth >= 0.99999)
    {
        color = FogColor;                                       // background = fog colour (tone-mapped below)
    }
    else
    {
        float3 N = normalize(nrm.xyz * 2 - 1);
        float3 P = ReconstructWorld(input.TexCoord, depth);
        float3 V = normalize(CameraPosition - P);
        float rim = pow(1.0 - saturate(dot(N, V)), 3.5);
        color = albedo * light.rgb + light.a + RimColor * rim * (0.15 + 0.85 * albedo);
        // Emissive: shininess channel saturated with zero specular marks glow materials.
        if (nrm.a > 0.995 && alb.a < 0.005) color = albedo * 2.5;
        float dist = distance(CameraPosition, P);
        float fog = saturate((dist - FogStart) / (FogEnd - FogStart));
        color = lerp(color, FogColor, fog);
    }
    color = 1.0 - exp(-color * 1.5);
    color = pow(saturate(color), 1.0 / 2.2);
    return float4(color, 1);
}

technique Directional { pass P0 { VertexShader = compile VS_SHADERMODEL QuadVS();   PixelShader = compile PS_SHADERMODEL DirectionalPS(); } }
technique PointLight  { pass P0 { VertexShader = compile VS_SHADERMODEL VolumeVS(); PixelShader = compile PS_SHADERMODEL PointPS(); } }
technique Composite   { pass P0 { VertexShader = compile VS_SHADERMODEL QuadVS();   PixelShader = compile PS_SHADERMODEL CompositePS(); } }
