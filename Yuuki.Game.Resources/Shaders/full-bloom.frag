#version 450

// A Full Bloom round the results ring, as light. While the ring gathers itself, two white rings close in on it over and
// over, faster as they near and brighter as the gather builds, and its middle fills with light. Then the bloom: a
// white-hot flash, and six rings in the heroes' colors bursting out one after another, past the screen's edges, each a
// bright crest with a glow trailing in behind it, never quite round, thinning as it goes. On its vertices, the same on
// each but v_TexCoords:
//   v_TexCoords  where, from the middle: 1 at the edge of the ring's box
//   v_Color      x how far the gather has built, 0–1 (negative: not gathering), y seconds since the bloom (negative: not
//                yet), z the time in seconds, w how bright
//   v_TexIndex   where round the heroes' colors the bloom's rings start (0–1)

#include "yuuki.glsl"

// A ring's crest at crestAt, `width` sharp, with a glow trailing in behind it.
float shock(float r, float crestAt, float width, float trailing)
{
    float d = r - crestAt;
    float crest = exp(-(d * d) / (width * width));
    float trail = exp(min(d, 0.0) / trailing) * (1.0 - smoothstep(0.0, 2.0 * width, d));
    return crest + 0.35 * trail;
}

void main()
{
    vec2 p = v_TexCoords;
    float gather = v_Color.x;
    float since = v_Color.y;
    float time = v_Color.z;
    float alpha = v_Color.w;
    float firstHue = v_TexIndex;

    float r = length(p);
    float angle = atan(p.y, p.x);
    vec2 around = vec2(cos(angle), sin(angle));

    // Brighter here and fainter there round the way, as the landing's rings are.
    float uneven = 0.6 + 0.6 * noise(vec3(around * 3.0, time * 1.4));

    vec3 light = vec3(0.0);

    if (gather >= 0.0)
    {
        for (int i = 0; i < 2; i++)
        {
            float phase = fract(gather * 1.6 + float(i) * 0.5);
            float crestAt = 3.45 - 2.27 * phase * phase;
            float width = 0.012 + 0.02 * phase;
            light += vec3(shock(r, crestAt, width, 0.08)) * 0.55 * sin(phase * PI) * gather * uneven;
        }

        light += vec3(0.35 * gather * gather * exp(-pow(r / 0.7, 2.0)));
    }

    if (since >= 0.0)
    {
        light += vec3(1.2 * exp(-since / 0.15) * (0.6 * exp(-pow(r / 0.9, 2.0)) + 0.6 * exp(-pow(r / 0.3, 2.0))));

        for (int i = 0; i < 6; i++)
        {
            float progress = since - float(i) * 0.055;

            if (progress <= 0.0 || progress >= 1.0)
                continue;

            float eased = 1.0 - pow(1.0 - progress, 3.0);
            float wobble = (noise(vec3(around * 2.0 + float(i) * 5.0, time)) - 0.5) * 0.08 * (0.3 + progress);
            float crestAt = 1.09 + 6.8 * eased + wobble;
            float width = 0.012 + 0.08 * (1.0 - eased);

            // White-hot at the very crest, the hero's color behind it.
            float d = r - crestAt;
            vec3 color = mix(heroColor(firstHue + float(i) / 6.0, 0.15), vec3(1.0), 0.4 * exp(-(d * d) / (width * width)));
            light += color * shock(r, crestAt, width, 0.1 + 0.3 * progress) * 0.9 * (1.0 - progress) * uneven;
        }
    }

    writeLight(light * alpha);
}
