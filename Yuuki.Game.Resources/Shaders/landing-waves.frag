#version 450

// What a rank landing sends out across the results ring, as light. A soft white-hot bloom from the middle, and three
// shock rings thrown out past the ring, one after another as ripples are: each a sharp bright crest with a glow trailing
// in behind it, never quite round, brighter here and fainter there, and thinning and fading as it spreads. The first
// has a thin rainbow fringe just outside its crest, in the heroes' colors as the bubble's film is, and sparkles riding
// it. A rank breaking flashes red and sends out a jagged red ring, broken in places. On its vertices, v_TexCoords says
// where, from the middle: 1 at the edge of the ring's box.

#include "yuuki.glsl"

layout(set = 0, binding = 5, std140) uniform LandingWavesBlock
{
    vec4 u_Color;         // The landing's color, and how bright it is.
    float u_SinceLanding; // Seconds since the landing (negative: not yet).
    float u_SinceBreak;   // Seconds since the break (negative: not yet).
    float u_Time;         // In seconds.
    float u_Padding;
};

// JudgementColors.MISS (#D9534F), linear.
const vec3 BREAK_RED = vec3(0.693, 0.087, 0.078);

float outCubic(float x)
{
    return 1.0 - pow(1.0 - x, 3.0);
}

// One shock ring, `since` seconds after the landing: how bright its crest and the glow behind it are at distance r,
// round the way `around` points. Out of `edge`, how sharp its crest is, and of `d`, how far past its crest r is.
vec2 shockRing(float r, vec2 around, float since, float delay, float duration, float from, float to, float seed, float time,
               out float edge, out float d, out float progress)
{
    progress = (since - delay) / duration;
    edge = 1.0;
    d = 1e3;

    if (progress <= 0.0 || progress >= 1.0)
        return vec2(0.0);

    // Never quite round: the crest runs a little ahead here and behind there, more the further it's spread.
    float wobble = (noise(vec3(around * 2.2 + seed, time * 0.8 + seed)) - 0.5) * 0.05 * (0.4 + progress);
    float crestAt = mix(from, to, outCubic(progress)) + wobble;

    d = r - crestAt;

    // A sharp crest that widens as it spreads, and a glow trailing in behind it that lengthens.
    edge = 0.010 + 0.014 * progress;
    float trailing = 0.04 + 0.12 * progress;

    float crest = exp(-(d * d) / (edge * edge));
    float trail = exp(min(d, 0.0) / trailing) * (1.0 - smoothstep(0.0, 2.0 * edge, d));

    // Its energy spreads round a longer way as it goes, and dies away; brighter in some places than others.
    float strength = pow(1.0 - progress, 1.6) / (0.5 + 0.5 * crestAt);
    float uneven = 0.5 + 0.7 * noise(vec3(around * 3.0 + seed * 2.0, time * 1.2));

    return vec2(crest, trail) * strength * uneven;
}

void main()
{
    vec2 p = v_TexCoords;
    float sinceLanding = u_SinceLanding;
    float sinceBreak = u_SinceBreak;
    float time = u_Time;
    float alpha = u_Color.a;
    vec3 color = toLinear(u_Color.rgb);

    float r = length(p);
    float angle = atan(p.y, p.x);
    vec2 around = vec2(cos(angle), sin(angle));

    vec3 light = vec3(0.0);

    if (sinceLanding >= 0.0)
    {
        // The bloom: white-hot in the middle, softening out over the ring's face.
        float flash = 0.85 * exp(-sinceLanding / 0.16);
        float bloom = 0.75 * exp(-pow(r / 0.62, 2.0)) + 0.5 * exp(-pow(r / 0.22, 2.0));
        light += mix(color, vec3(1.0), 0.75) * flash * bloom;

        // Three rings, each later, slower, shorter and fainter than the last.
        float delays[3] = float[3](0.0, 0.09, 0.2);
        float durations[3] = float[3](0.75, 0.8, 0.85);
        float froms[3] = float[3](0.72, 0.8, 0.86);
        float tos[3] = float[3](2.25, 1.75, 1.45);
        float strengths[3] = float[3](1.0, 0.55, 0.32);

        for (int i = 0; i < 3; i++)
        {
            float edge, d, progress;
            vec2 ring = shockRing(r, around, sinceLanding, delays[i], durations[i], froms[i], tos[i], float(i) * 7.3, time, edge, d, progress);

            if (ring.x + ring.y <= 0.0)
                continue;

            // White-hot at the very crest, the landing's color behind it.
            vec3 crestColor = mix(color, vec3(1.0), 0.6 * ring.x);
            light += (crestColor * ring.x * 1.3 + color * ring.y * 0.3) * strengths[i];

            if (i != 0)
                continue;

            float fade = pow(1.0 - progress, 1.6);

            // A thin rainbow fringe just outside the crest, its colors running round and across it as a soap film's do.
            float fringe = exp(-pow((d - 2.6 * edge) / (1.6 * edge), 2.0));
            light += heroColor(angle / (2.0 * PI) + time * 0.15 + d * 4.0, 0.15) * fringe * 0.6 * fade;

            // Sparkles riding the crest: in some of the stretches round it, a four-point star, twinkling.
            const float stretches = 14.0;
            float turn = angle / (2.0 * PI) + 0.5;
            float stretch = floor(turn * stretches);
            float centre = (stretch + 0.5 + (hash(vec2(stretch, 3.0)) - 0.5) * 0.6) / stretches;

            if (hash(vec2(stretch, 11.0)) > 0.45)
            {
                float crestAt = r - d;
                float u = (turn - centre) * 2.0 * PI * crestAt;
                float twinkle = 0.55 + 0.45 * sin(time * 9.0 + hash(vec2(stretch, 5.0)) * 6.28);
                float life = smoothstep(0.02, 0.12, progress) * (1.0 - smoothstep(0.45, 0.75, progress));
                float size = 0.016;
                float star = 2.2 * exp(-(u * u + d * d) / (size * size * 0.4))
                             + 1.1 * exp(-pow(u / (5.0 * size), 2.0) - pow(d / (0.6 * size), 2.0))
                             + 1.1 * exp(-pow(d / (5.0 * size), 2.0) - pow(u / (0.6 * size), 2.0));
                vec3 sparkle = mix(vec3(1.0), heroColor(stretch / stretches, 0.3), 0.3);
                light += sparkle * star * twinkle * life;
            }
        }
    }

    if (sinceBreak >= 0.0 && sinceBreak < 0.6)
    {
        // The break: a red flash, and a jagged red ring cracking out, broken in places.
        light += BREAK_RED * 0.55 * exp(-sinceBreak / 0.12) * exp(-pow(r / 0.6, 2.0));

        float progress = sinceBreak / 0.42;

        if (progress < 1.0)
        {
            float jagged = (noise(vec3(around * 6.0 + 31.0, sinceBreak * 2.0)) - 0.5) * 0.12;
            float d = r - (0.55 + 0.75 * outCubic(progress) + jagged);
            float crest = exp(-pow(d / 0.015, 2.0));
            float unbroken = smoothstep(0.35, 0.55, noise(vec3(around * 4.0 + 50.0, 0.0)));
            light += BREAK_RED * crest * unbroken * 1.2 * pow(1.0 - progress, 2.0);
        }
    }

    writeLight(light * alpha);
}
