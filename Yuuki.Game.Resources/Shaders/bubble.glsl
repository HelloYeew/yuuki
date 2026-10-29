// A bubble's film, shared by its two halves: bubble-back.frag draws what's behind whatever is inside the bubble,
// bubble-front.frag what's in front of it. On every vertex, the same on each:
//   v_TexCoords  where on the bubble, in its own shape: 1 at the edge however it wobbles
//   v_Color      x the time in seconds, y how opaque (negative: greyed), z a wob's flash, w how bright the glow is
//   v_TexIndex   how bright the film is (its colors, rim and glints)

#include "yuuki.glsl"

// The film's colors run through the heroes' as a soap film's run through the spectrum.
vec3 film(float position)
{
    return heroColor(position, 0.25);
}

// How thick the film is at a point, which picks its color: swirling slowly, and thicker toward the bottom, where
// it drains to.
float thickness(vec2 p, float time)
{
    vec2 q = p * 1.4;
    vec2 warp = vec2(fbm(q + vec2(0.0, time * 0.11)), fbm(q + vec2(5.2, -time * 0.09)));
    return fbm(q * 1.2 + 1.6 * warp + vec2(time * 0.05, 0.0)) + 0.3 * p.y;
}

vec3 filmAt(vec2 p, float time, bool greyed)
{
    vec3 color = film(thickness(p, time) * 2.2 + time * 0.03);
    return greyed ? vec3(dot(color, vec3(0.2126, 0.7152, 0.0722))) : color;
}

// How far round from one angle to another, -PI to PI.
float angleTo(float from, float to)
{
    return mod(to - from + PI, 2.0 * PI) - PI;
}

void drawFilm(bool front)
{
    vec2 p = v_TexCoords;
    float time = v_Color.x;
    float alpha = abs(v_Color.y);
    bool greyed = v_Color.y < 0.0;
    float flash = v_Color.z;
    float glowStrength = v_Color.w;
    float sheen = v_TexIndex;

    float s = length(p);
    float aa = max(fwidth(s), 0.0001);
    float inside = 1.0 - smoothstep(1.0 - aa, 1.0 + aa, s);
    float outside = max(s - 1.0, 0.0);

    vec3 color = filmAt(p, time, greyed);
    float angle = atan(p.y, p.x);
    float sway = 0.08 * sin(time * 0.5);
    vec3 light;

    if (front)
    {
        // The film shows strongly toward the rim, where it's seen edge on, and a thin bright line runs just inside
        // the edge.
        float edgeOn = pow(clamp(s, 0.0, 1.0), 14.0);
        float rim = 0.32 * edgeOn * (1.0 + 1.2 * flash);
        float line = exp(-pow((s - 0.992) / 0.007, 2.0)) * (0.22 + 0.4 * flash);
        light = (color * rim + mix(color, vec3(1.0), 0.5) * line) * inside * sheen;

        // Glints on the near side: a curve of reflected light up and to the left, and a small bright spot at its end.
        float curve = exp(-pow(angleTo(-2.36 + sway, angle) / 0.33, 2.0)) * exp(-pow((s - 0.8) / 0.032, 2.0));
        vec2 spotAt = 0.74 * vec2(cos(-1.9 + sway), sin(-1.9 + sway));
        float spot = exp(-dot(p - spotAt, p - spotAt) / 0.0008);
        light += vec3(0.35 * curve + 0.45 * spot) * inside * sheen;

        // The glow round it: a close bright one and a wide faint one, in the colors of the rim nearest.
        float glow = 0.16 * exp(-outside / 0.03) + 0.03 * exp(-outside / 0.09);
        vec3 rimColor = s > 1.0 ? filmAt(p / s, time, greyed) : color;
        light += rimColor * glow * glowStrength * (1.0 + flash) * (1.0 - inside);
    }
    else
    {
        // The far side, seen through the bubble: its film faint over the face, and a faint glint down and to the right
        // where it reflects the light the near side shows up and to the left.
        float opposite = exp(-pow(angleTo(0.78 + sway, angle) / 0.25, 2.0)) * exp(-pow((s - 0.9) / 0.025, 2.0));
        light = (color * 0.012 + vec3(0.15 * opposite)) * inside * sheen;
    }

    writeLight(light * alpha);
}
