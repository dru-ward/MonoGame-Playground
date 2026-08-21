//=====================================================================================================
//  Deferred.fx  -  Multi-technique effect for the MonoGame "Deferred-style 2.5D" demo
//
//  Techniques
//    PointLight   : one dynamic light, additive-accumulated into the light buffer (per-pixel, normal mapped)
//    MultiLight   : up to MAX_LIGHTS lights evaluated in a single pass (fixed-count unrolled loop)
//    Composite    : Albedo * Diffuse + Specular  -> lit scene
//    BloomExtract : bright-pass with soft knee
//    GaussianBlur : separable 15-tap gaussian (offsets & weights uploaded from C#)
//    FinalCombine : scene + bloom, then vignette
//    Blit         : straight copy (used for the debug buffer views)
//
//  Coordinate conventions
//    * All post/light passes draw a quad whose vertex positions are in *pixel* space (0..W, 0..H).
//      The C# side supplies WorldViewProjection = orthographic(0,W,H,0) so pixel (x,y) maps to clip space.
//    * "Screen space" == render-target pixel space. Light positions are pre-transformed by the camera
//      view matrix on the CPU, so lights, pixels and normal-map normals all live in the same space and
//      no per-pixel matrix multiply is required.
//    * Normal maps are tangent space, encoded n*0.5+0.5. Because sprites are drawn axis aligned and the
//      camera never rotates, tangent space == screen space, so decoded normals are used directly.
//      +X = right, +Y = *down* (screen), +Z = toward the viewer.
//=====================================================================================================

#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_3
    #define PS_SHADERMODEL ps_4_0_level_9_3
#endif

#define MAX_LIGHTS   8
#define BLUR_TAPS    15

//----------------------------------------------------------------- shared parameters
float4x4 WorldViewProjection;      // pixel-space -> clip-space (orthographic)
float2   ScreenSize;               // render target size in pixels (used to turn UV into pixel coords)

//----------------------------------------------------------------- textures / samplers
// NOTE: sampler_state blocks intentionally omit Filter/Address so the C# GraphicsDevice.SamplerStates[]
// (the "Advanced State Management" part of the demo) controls filtering & wrapping.
Texture2D AlbedoTex;   sampler AlbedoSampler : register(s0) = sampler_state { Texture = <AlbedoTex>; };
Texture2D NormalTex;   sampler NormalSampler : register(s1) = sampler_state { Texture = <NormalTex>; };
Texture2D LightTex;    sampler LightSampler  : register(s2) = sampler_state { Texture = <LightTex>;  };
Texture2D BloomTex;    sampler BloomSampler  : register(s3) = sampler_state { Texture = <BloomTex>;  };

//----------------------------------------------------------------- sprite normal rotation
float2 NormalRotation = float2(1.0, 0.0);   // (cos, sin) of the sprite rotation

//----------------------------------------------------------------- lighting parameters
float3 LightPosition;              // screen-space px (x,y) + height above the plane (z, in px)
float3 LightColor;                 // linear RGB
float  LightRadius;                // px, hard cutoff radius
float  LightIntensity;             // scalar gain
float2 LightDir   = float2(1.0, 0.0);      // spot direction (screen space); ignored for omni lights
float2 LightCone  = float2(-2.0, -2.0);    // (cosOuter, cosInner); cosOuter <= -1 => omni
float  SpecularPower  = 32.0;
float  SpecularAmount = 0.35;

// single-pass variant
float3 LightPositions[MAX_LIGHTS];
float3 LightColors[MAX_LIGHTS];
float2 LightRadiusIntensity[MAX_LIGHTS];   // x = radius, y = intensity
float2 LightDirs[MAX_LIGHTS];
float2 LightCones[MAX_LIGHTS];

//----------------------------------------------------------------- post parameters
float  BloomThreshold  = 0.6;
float  BloomSoftKnee   = 0.5;
float  BloomIntensity  = 1.2;
float2 SampleOffsets[BLUR_TAPS];   // UV offsets for the separable blur
float  SampleWeights[BLUR_TAPS];   // matching gaussian weights (sum == 1)
float  VignetteRadius   = 0.75;    // normalised radius (corners == 1) where darkening is complete
float  VignetteSoftness = 0.45;
float  VignetteStrength = 0.65;    // 0 = off, 1 = fully black corners
float  Exposure         = 1.0;
// colour grade (set from C#): desaturation, split-tone multipliers, contrast, film grain
float  Desaturate       = 0.0;
float3 GradeShadows     = float3(1.0, 1.0, 1.0);
float3 GradeHighlights  = float3(1.0, 1.0, 1.0);
float  Contrast         = 1.0;
float  GrainAmount      = 0.0;
float  Time             = 0.0;

//=====================================================================================================
//  Vertex shader shared by every technique
//=====================================================================================================
struct VSInput  { float4 Position : POSITION0; float2 TexCoord : TEXCOORD0; };
struct VSOutput { float4 Position : SV_POSITION; float2 TexCoord : TEXCOORD0; };

VSOutput MainVS(VSInput input)
{
    VSOutput o;
    // Pixel space -> clip space. WVP is orthographic so this is a pure scale/translate:
    //   x_clip = 2x/W - 1,  y_clip = 1 - 2y/H
    o.Position = mul(input.Position, WorldViewProjection);
    o.TexCoord = input.TexCoord;
    return o;
}

//=====================================================================================================
//  Lighting helpers
//=====================================================================================================
// Decodes an encoded tangent-space normal and re-normalises it.
float3 DecodeNormal(float2 uv)
{
    float3 n = tex2D(NormalSampler, uv).xyz * 2.0 - 1.0;
    return normalize(n);
}

// Evaluates one point light at surface pixel P (z = 0 plane) with normal N.
// Returns diffuse RGB in .rgb and monochrome specular in .a so a single Color RT holds both.
float4 ShadePointLight(float3 P, float3 N, float3 Lpos, float3 Lcol, float radius, float intensity, float2 dir, float2 cone)
{
    float3 toLight = Lpos - P;                        // vector surface -> light (px)
    float  dist    = length(toLight);
    float3 L       = toLight / max(dist, 1e-4);

    // Smooth quadratic falloff that reaches exactly zero at 'radius' (keeps scissor clipping seamless):
    //   atten = (1 - (d/r)^2)^2   for d < r
    float  x     = saturate(1.0 - (dist * dist) / (radius * radius));
    float  atten = x * x * intensity;

    // Spot cone (torches): fade between cosOuter and cosInner around 'dir'; omni lights pass cosOuter <= -1.
    if (cone.x > -1.0)
    {
        float2 toPix = normalize(P.xy - Lpos.xy + float2(1e-4, 0.0));
        float  c = dot(toPix, dir);
        atten *= smoothstep(cone.x, cone.y, c);
        atten *= saturate(dist / 28.0);               // no hotspot right under the torch body
    }

    // Lambert
    float NdotL = saturate(dot(N, L));

    // Blinn-Phong: the camera looks straight down the +Z axis, so V = (0,0,1)
    float3 V = float3(0.0, 0.0, 1.0);
    float3 H = normalize(L + V);
    float  spec = pow(saturate(dot(N, H)), SpecularPower) * SpecularAmount * step(0.001, NdotL);

    return float4(Lcol * NdotL * atten, spec * atten * dot(Lcol, float3(0.299, 0.587, 0.114)));
}

//=====================================================================================================
//  Technique: PointLight  (one light per draw, additive blend accumulates into the light buffer)
//=====================================================================================================
float4 PointLightPS(VSOutput input) : COLOR0
{
    float3 P = float3(input.TexCoord * ScreenSize, 0.0);   // pixel position on the z=0 plane
    float3 N = DecodeNormal(input.TexCoord);
    return ShadePointLight(P, N, LightPosition, LightColor, LightRadius, LightIntensity, LightDir, LightCone);
}

technique PointLight
{
    pass P0 { VertexShader = compile VS_SHADERMODEL MainVS(); PixelShader = compile PS_SHADERMODEL PointLightPS(); }
}

//=====================================================================================================
//  Technique: MultiLight  (all lights in one pass; loop count is a compile-time constant so ps_3_0 is happy)
//=====================================================================================================
float4 MultiLightPS(VSOutput input) : COLOR0
{
    float3 P = float3(input.TexCoord * ScreenSize, 0.0);
    float3 N = DecodeNormal(input.TexCoord);
    float4 acc = 0;
    [unroll]
    for (int i = 0; i < MAX_LIGHTS; i++)
    {
        // Unused slots are uploaded with intensity 0 by the CPU, so no dynamic branch is needed.
        acc += ShadePointLight(P, N, LightPositions[i], LightColors[i],
                               LightRadiusIntensity[i].x, LightRadiusIntensity[i].y, LightDirs[i], LightCones[i]);
    }
    return acc;
}

technique MultiLight
{
    pass P0 { VertexShader = compile VS_SHADERMODEL MainVS(); PixelShader = compile PS_SHADERMODEL MultiLightPS(); }
}

//=====================================================================================================
//  Technique: Composite   (Albedo * diffuse + specular)
//=====================================================================================================
float4 CompositePS(VSOutput input) : COLOR0
{
    float4 albedo = tex2D(AlbedoSampler, input.TexCoord);
    float4 light  = tex2D(LightSampler,  input.TexCoord);
    float3 color  = albedo.rgb * light.rgb + light.aaa;     // .a carries accumulated specular
    return float4(color * Exposure, 1.0);
}

technique Composite
{
    pass P0 { VertexShader = compile VS_SHADERMODEL MainVS(); PixelShader = compile PS_SHADERMODEL CompositePS(); }
}

//=====================================================================================================
//  Technique: BloomExtract  (bright pass with a soft knee so the threshold does not "pop")
//=====================================================================================================
float4 BloomExtractPS(VSOutput input) : COLOR0
{
    float3 c = tex2D(AlbedoSampler, input.TexCoord).rgb;   // AlbedoSampler is reused as the generic "source" slot
    float  brightness = max(c.r, max(c.g, c.b));
    float  knee = BloomThreshold * BloomSoftKnee;
    float  soft = brightness - BloomThreshold + knee;
    soft = clamp(soft, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee + 1e-4);
    float  contribution = max(soft, brightness - BloomThreshold) / max(brightness, 1e-4);
    return float4(c * contribution, 1.0);
}

technique BloomExtract
{
    pass P0 { VertexShader = compile VS_SHADERMODEL MainVS(); PixelShader = compile PS_SHADERMODEL BloomExtractPS(); }
}

//=====================================================================================================
//  Technique: GaussianBlur  (separable; C# sets SampleOffsets to (dx,0) or (0,dy))
//=====================================================================================================
float4 GaussianBlurPS(VSOutput input) : COLOR0
{
    float4 c = 0;
    [unroll]
    for (int i = 0; i < BLUR_TAPS; i++)
        c += tex2D(AlbedoSampler, input.TexCoord + SampleOffsets[i]) * SampleWeights[i];
    return c;
}

technique GaussianBlur
{
    pass P0 { VertexShader = compile VS_SHADERMODEL MainVS(); PixelShader = compile PS_SHADERMODEL GaussianBlurPS(); }
}

//=====================================================================================================
//  Technique: FinalCombine   (scene + bloom, clamp, vignette)
//=====================================================================================================
float4 FinalCombinePS(VSOutput input) : COLOR0
{
    float3 scene = tex2D(AlbedoSampler, input.TexCoord).rgb;
    float3 bloom = tex2D(BloomSampler,  input.TexCoord).rgb;
    float3 color = scene + bloom * BloomIntensity;

    // ---- grade: desaturate, split-tone (cool shadows / warm highlights), contrast, grain ----------------
    float lum = dot(color, float3(0.299, 0.587, 0.114));
    color = lerp(color, lum.xxx, Desaturate);
    color = lerp(color * GradeShadows, color * GradeHighlights, saturate(lum * 1.6));
    color = (color - 0.5) * Contrast + 0.5;
    float2 gp = input.TexCoord * ScreenSize + frac(Time * 0.37) * 1000.0;
    float grain = frac(sin(dot(gp, float2(12.9898, 78.233))) * 43758.5453) - 0.5;
    color += grain * GrainAmount * (1.0 - lum * 0.6);        // grain shows mostly in the darks

    // Vignette: radial distance from centre, smooth ramp between (radius - softness) and radius.
    float2 d    = input.TexCoord - 0.5;
    float  dist = length(d) * 1.4142;                 // normalise so corners == 1.0
    float  vig  = 1.0 - smoothstep(VignetteRadius - VignetteSoftness, VignetteRadius, dist) * VignetteStrength;
    color *= vig;

    return float4(saturate(color), 1.0);
}

technique FinalCombine
{
    pass P0 { VertexShader = compile VS_SHADERMODEL MainVS(); PixelShader = compile PS_SHADERMODEL FinalCombinePS(); }
}

//=====================================================================================================
//  Technique: SpriteNormalRotate  (pixel-shader-only: SpriteBatch supplies its own vertex shader)
//  A sprite drawn with a rotation still has its normal map in *texture* space; this rotates the decoded
//  tangent-space normal by the same angle so the lighting pass sees screen-space normals.
//  Input alpha is PREMULTIPLIED (SpriteBatch AlphaBlend), so un-premultiply before decoding.
//=====================================================================================================
float4 SpriteNormalRotatePS(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 c = tex2D(AlbedoSampler, texCoord);          // s0 = the sprite texture bound by SpriteBatch
    float  a = c.a;
    float3 n = (c.rgb / max(a, 1e-4)) * 2.0 - 1.0;
    float2 r = float2(n.x * NormalRotation.x - n.y * NormalRotation.y,
                      n.x * NormalRotation.y + n.y * NormalRotation.x);
    float3 rn = normalize(float3(r, n.z));
    return float4((rn * 0.5 + 0.5) * a, a) * color.a;   // keep premultiplied for the AlphaBlend edge
}

technique SpriteNormalRotate
{
    pass P0 { PixelShader = compile PS_SHADERMODEL SpriteNormalRotatePS(); }
}

//=====================================================================================================
//  Technique: Blit  (debug views)
//=====================================================================================================
float4 BlitPS(VSOutput input) : COLOR0
{
    return float4(tex2D(AlbedoSampler, input.TexCoord).rgb, 1.0);
}

technique Blit
{
    pass P0 { VertexShader = compile VS_SHADERMODEL MainVS(); PixelShader = compile PS_SHADERMODEL BlitPS(); }
}
