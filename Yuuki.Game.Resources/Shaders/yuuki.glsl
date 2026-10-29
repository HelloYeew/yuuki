// What every game shader starts from. Each pairs with the framework's shader.vert, takes its inputs, and adds its
// light with writeLight (which keeps it inside any mask, as the framework's own drawing is).
//
// Game shaders can't have uniform blocks of their own on every backend (Metal and Direct3D 11 bind only the
// framework's), so whatever a shader would take as uniforms comes in on its vertices instead: each shader says
// what its v_Color, v_TexCoords and v_TexIndex carry. Every shader must read all of its inputs: Direct3D links
// them to the vertex shader's outputs by register, and an input the compiler dropped as unread would move every
// later one onto the wrong output. GameShadersTest checks this for each shader.

#include "sh_Utils.glsl"

layout(location = 0) in vec4 v_Color;
layout(location = 1) in vec2 v_TexCoords;
layout(location = 2) in vec2 v_FragPos;
layout(location = 3) in float v_TexIndex;
layout(location = 4) in vec4 v_ClipData;
layout(location = 5) in float v_ClipShearX;
layout(location = 6) in float v_ClipRadius;
layout(location = 7) in vec4 v_ClipFadeEdges;
layout(location = 8) in vec4 v_ClipFadeLength;

layout(location = 0) out vec4 FragColor;

const float PI = 3.14159265;

vec3 toLinear(vec3 srgb)
{
    return pow(srgb, vec3(2.2));
}

// The heroes' colors in the order they cycle (HeroColors.CYCLE), blended smoothly round: a whole turn of
// position is all six. Lightened by white, and linear.
vec3 heroColor(float position, float white)
{
    vec3 colors[6] = vec3[6](
        vec3(0.925, 0.584, 0.608),  // Yuuna  #EC959B
        vec3(0.910, 0.745, 0.345),  // Fuu    #E8BE58
        vec3(0.725, 0.796, 0.471),  // Itsuki #B9CB78
        vec3(0.620, 0.765, 0.808),  // Mimori #9EC3CE (her light one sinc her main is too dark to light anything)
        vec3(0.800, 0.365, 0.631),  // Sonoko #CC5DA1
        vec3(0.875, 0.569, 0.475)); // Karin  #DF9179

    float at = fract(position) * 6.0;
    int i = int(at);
    vec3 color = mix(colors[i], colors[(i + 1) % 6], smoothstep(0.0, 1.0, at - float(i)));
    return toLinear(mix(color, vec3(1.0), white));
}

float hash(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float noise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);

    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
               mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p)
{
    float value = 0.0;
    float amplitude = 0.5;

    for (int i = 0; i < 3; i++)
    {
        value += amplitude * noise(p);
        p = p * 2.03 + vec2(1.7, 9.2);
        amplitude *= 0.5;
    }

    return value;
}

// Writes a pixel of added light, faded and clipped by any mask it's drawn in.
void writeLight(vec3 light)
{
    FragColor = vec4(light, 1.0);
    FragColor.a *= clipFade(v_FragPos, v_ClipFadeEdges, v_ClipFadeLength);

    if (!applyClipping(v_FragPos, v_ClipData, v_ClipShearX, v_ClipRadius, FragColor))
        discard;
}
