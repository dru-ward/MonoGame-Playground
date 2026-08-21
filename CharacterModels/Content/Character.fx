// Skinned character shader: 4-weight GPU skinning, hemisphere ambient, wrap-diffuse,
// Blinn-Phong specular, rim light, PCF shadow map, subtle procedural grain.
#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_3
    #define PS_SHADERMODEL ps_4_0_level_9_3
#endif

#define MAX_BONES 64

float4x4 World;
float4x4 View;
float4x4 Projection;
float4x4 LightViewProjection;
float4x3 Bones[MAX_BONES];

float3 LightDirection;      // direction the light travels (normalized)
float3 LightColor;
float3 FillDirection;
float3 FillColor;
float3 SkyColor;
float3 GroundColor;
float3 CameraPosition;
float3 RimColor;
float  ShadowMapSize;
float  ShadowStrength;
float  GrainStrength;
float  FogStart;
float  FogEnd;
float3 FogColor;
float  Time;
float  WindStrength;
float3 WindDirection;

texture ShadowMap;
sampler ShadowSampler = sampler_state
{
    Texture = <ShadowMap>;
    MinFilter = Point; MagFilter = Point; MipFilter = None;
    AddressU = Clamp; AddressV = Clamp;
};

texture GrainTexture;
sampler GrainSampler = sampler_state
{
    Texture = <GrainTexture>;
    MinFilter = Linear; MagFilter = Linear; MipFilter = Linear;
    AddressU = Wrap; AddressV = Wrap;
};

struct VSInput
{
    float4 Position     : POSITION0;
    float3 Normal       : NORMAL0;
    float4 Color        : COLOR0;
    float2 Material     : TEXCOORD0;   // x = specular strength, y = shininess (0..1)
    float4 BlendIndices : BLENDINDICES0;
    float4 BlendWeights : BLENDWEIGHT0;
};

struct VSOutput
{
    float4 Position  : SV_POSITION;
    float4 Color     : COLOR0;
    float3 Normal    : TEXCOORD0;
    float3 WorldPos  : TEXCOORD1;
    float4 LightPos  : TEXCOORD2;
    float3 ObjPos    : TEXCOORD3;
    float2 Material  : TEXCOORD4;
    float4 ClipPos   : TEXCOORD5;
};

// Foliage flutter: vertices with colour alpha < 1 ripple along their normal at high frequency.
// Phase comes from world position so neighbouring leaves move coherently and different trees differ.
void Flutter(inout float4 worldPos, float3 worldNrm, float alpha)
{
    float amount = (1.0 - alpha) * WindStrength;
    if (amount <= 0.0) return;
    float ph = dot(worldPos.xyz, float3(3.1, 2.3, 2.7));
    float wave = sin(Time * 7.0 + ph) * 0.6 + sin(Time * 11.3 + ph * 1.7) * 0.4;
    float gust = 0.6 + 0.4 * sin(Time * 0.9 + dot(worldPos.xyz, WindDirection) * 0.35);
    worldPos.xyz += worldNrm * wave * gust * amount * 0.07;
    worldPos.xyz += WindDirection * gust * amount * 0.03;
}

void Skin(inout float4 position, inout float3 normal, float4 indices, float4 weights)
{
    float4x3 skin = 0;
    skin += Bones[indices.x] * weights.x;
    skin += Bones[indices.y] * weights.y;
    skin += Bones[indices.z] * weights.z;
    skin += Bones[indices.w] * weights.w;
    position.xyz = mul(position, skin);
    normal = mul(normal, (float3x3)skin);
}

VSOutput MainVS(VSInput input)
{
    VSOutput o;
    float4 pos = input.Position;
    float3 nrm = input.Normal;
    o.ObjPos = pos.xyz;
    Skin(pos, nrm, input.BlendIndices, input.BlendWeights);

    float4 worldPos = mul(pos, World);
    o.Normal = normalize(mul(nrm, (float3x3)World));
    Flutter(worldPos, o.Normal, input.Color.a);
    o.WorldPos = worldPos.xyz;
    o.Position = mul(mul(worldPos, View), Projection);
    o.ClipPos = o.Position;
    o.Color = input.Color;
    o.LightPos = mul(worldPos, LightViewProjection);
    o.Material = input.Material;
    return o;
}

float SampleShadow(float4 lightPos, float ndotl)
{
    float3 proj = lightPos.xyz / lightPos.w;
    float2 uv = float2(proj.x * 0.5 + 0.5, 0.5 - proj.y * 0.5);
    if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return 1.0;
    float depth = proj.z;
    float bias = max(0.0015, 0.006 * (1.0 - ndotl));
    float texel = 1.0 / ShadowMapSize;
    float lit = 0;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float d = tex2D(ShadowSampler, uv + float2(x, y) * texel).r;
            lit += (depth - bias <= d) ? 1.0 : 0.0;
        }
    }
    return lit / 9.0;
}

float4 MainPS(VSOutput input) : COLOR0
{
    float3 N = normalize(input.Normal);
    float3 V = normalize(CameraPosition - input.WorldPos);
    float3 L = -normalize(LightDirection);
    float3 H = normalize(L + V);

    float ndotl = dot(N, L);
    float shadow = lerp(1.0, SampleShadow(input.LightPos, ndotl), ShadowStrength);

    // Procedural grain (object-space, so it sticks to the skinned mesh).
    float3 op = input.ObjPos * 6.0;
    float g = tex2D(GrainSampler, op.xy).r * 0.5 + tex2D(GrainSampler, op.zy + 0.37).r * 0.5;
    float3 albedo = pow(input.Color.rgb, 2.2) * lerp(1.0, 0.75 + 0.5 * g, GrainStrength);

    // Wrap diffuse (soft terminator, good for characters).
    float wrap = 0.15;
    float diff = saturate((ndotl + wrap) / (1.0 + wrap));
    float3 direct = LightColor * diff * shadow;

    float fillD = saturate(dot(N, -normalize(FillDirection)) * 0.5 + 0.5);
    float3 fill = FillColor * fillD;

    float hemi = N.y * 0.5 + 0.5;
    float3 ambient = lerp(GroundColor, SkyColor, hemi);

    float specStrength = input.Material.x;
    float shininess = lerp(8.0, 160.0, input.Material.y);
    float spec = pow(saturate(dot(N, H)), shininess) * specStrength * shadow * saturate(ndotl * 4.0);
    // Fresnel-ish boost on glancing angles for metals.
    spec *= 1.0 + 2.0 * pow(1.0 - saturate(dot(N, V)), 3.0) * specStrength;

    float rim = pow(1.0 - saturate(dot(N, V)), 3.5) * saturate(0.4 + 0.6 * dot(N, L) * 0.5 + 0.5);
    float3 rimLight = RimColor * rim;

    float3 color = albedo * (direct + fill + ambient) + LightColor * spec + rimLight * (0.15 + 0.85 * albedo);

    float dist = distance(CameraPosition, input.WorldPos);
    float fog = saturate((dist - FogStart) / (FogEnd - FogStart));
    color = lerp(color, FogColor, fog);

    // Exposure tone map + gamma.
    color = 1.0 - exp(-color * 1.5);
    color = pow(saturate(color), 1.0 / 2.2);
    return float4(color, 1.0);   // alpha carries flutter weight, never opacity
}

// ---------------------------------------------------------------- G-buffer pass (deferred)
// RT0: albedo (sRGB) + specular strength, RT1: world normal*0.5+0.5 + shininess, RT2: clip depth z/w (Single).
struct GBufferOut
{
    float4 Albedo : COLOR0;
    float4 Normal : COLOR1;
    float4 Depth  : COLOR2;
};

GBufferOut GBufferPS(VSOutput input)
{
    GBufferOut o;
    float3 op = input.ObjPos * 6.0;
    float g = tex2D(GrainSampler, op.xy).r * 0.5 + tex2D(GrainSampler, op.zy + 0.37).r * 0.5;
    float3 albedo = input.Color.rgb * lerp(1.0, 0.75 + 0.5 * g, GrainStrength);
    o.Albedo = float4(albedo, input.Material.x);
    o.Normal = float4(normalize(input.Normal) * 0.5 + 0.5, input.Material.y);
    o.Depth = float4(input.ClipPos.z / input.ClipPos.w, 0, 0, 1);
    return o;
}

technique GBuffer
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL GBufferPS();
    }
}

// ---------------------------------------------------------------- shadow pass
struct ShadowVSOutput
{
    float4 Position : SV_POSITION;
    float  Depth    : TEXCOORD0;
};

ShadowVSOutput ShadowVS(VSInput input)
{
    ShadowVSOutput o;
    float4 pos = input.Position;
    float3 nrm = input.Normal;
    Skin(pos, nrm, input.BlendIndices, input.BlendWeights);
    float4 worldPos = mul(pos, World);
    Flutter(worldPos, normalize(mul(nrm, (float3x3)World)), input.Color.a);
    o.Position = mul(worldPos, LightViewProjection);
    o.Depth = o.Position.z / o.Position.w;
    return o;
}

float4 ShadowPS(ShadowVSOutput input) : COLOR0
{
    return float4(input.Depth, 0, 0, 1);
}

technique Skinned
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS();
    }
}

technique ShadowCaster
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL ShadowVS();
        PixelShader  = compile PS_SHADERMODEL ShadowPS();
    }
}
