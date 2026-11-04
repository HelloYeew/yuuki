// What every game shader starts from, on top of the framework's starting point: the game's colors and noise, and
// writeLight to add its light with. Each shader takes its values in a uniform block of its own, and says what's on its
// vertices.

#include "sh_Custom.glsl"

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

float hash(vec3 p)
{
    p = fract(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return fract((p.x + p.y) * p.z);
}

// Smooth noise in three dimensions: with time as the third, a pattern changes where it is instead of sliding along.
float noise(vec3 p)
{
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);

    float near = mix(mix(hash(i), hash(i + vec3(1.0, 0.0, 0.0)), u.x),
                     mix(hash(i + vec3(0.0, 1.0, 0.0)), hash(i + vec3(1.0, 1.0, 0.0)), u.x), u.y);
    float far = mix(mix(hash(i + vec3(0.0, 0.0, 1.0)), hash(i + vec3(1.0, 0.0, 1.0)), u.x),
                    mix(hash(i + vec3(0.0, 1.0, 1.0)), hash(i + vec3(1.0, 1.0, 1.0)), u.x), u.y);
    return mix(near, far, u.z);
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
    writeFragment(vec4(light, 1.0));
}
