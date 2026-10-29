#version 450

// Sunlight out from behind a rank that has just landed, as light through leaves and petals is: rays of every width,
// bunched here and spaced there, each swelling, narrowing, splitting and fading in its own time rather than the
// whole turning together, in a soft glow round the sun, the whole breathing slowly. On its vertices, the same on each
// but v_TexCoords:
//   v_TexCoords  where, from the middle: 1 at the edge of its box
//   v_Color      x about how many rays go round, y how narrow they are, z the time in seconds,
//                w how bright (negative: it takes the heroes' colors in turn round the circle)
//   v_TexIndex   the light's color, as LightRays packs it: six bits a channel, red highest

#include "yuuki.glsl"

// Six bits a channel keeps the packed number small enough that it comes through interpolation whole.
vec3 unpackColor(float packed)
{
    float v = round(packed);
    vec3 bits = vec3(floor(v / 4096.0), floor(mod(v, 4096.0) / 64.0), mod(v, 64.0));
    return toLinear(bits / 63.0);
}

void main()
{
    vec2 p = v_TexCoords;
    float density = v_Color.x;
    float narrowness = v_Color.y;
    float time = v_Color.z;
    float alpha = abs(v_Color.w);
    bool heroes = v_Color.w < 0.0;

    float r = length(p);
    float angle = atan(p.y, p.x);
    vec2 around = vec2(cos(angle), sin(angle));

    // The rays: noise read round a circle, so they join up all the way round. The circle drifts through the noise,
    // so the rays change shape where they are rather than move round; a second, finer set drifts its own way.
    float circle = density / (2.0 * PI);
    float broad = noise(around * circle + vec2(time * 0.07, -time * 0.05));
    float fine = noise(around * circle * 2.6 + vec2(17.0 - time * 0.11, time * 0.09));
    float rays = pow(broad, narrowness) + 0.4 * pow(fine, narrowness * 1.5);

    // Out from the middle: rising out of the glow, then dying away to nothing at the edge.
    float rise = smoothstep(0.02, 0.25, r);
    float fade = pow(max(1.0 - r, 0.0), 1.4);

    // The glow round the sun, and the slow breathing of it all.
    float glow = 0.35 * exp(-pow(r / 0.5, 2.0)) * fade;
    float breath = 0.88 + 0.12 * noise(vec2(time * 0.35, 3.1));

    // White hot close in, its own color further out.
    vec3 color = heroes ? heroColor(angle / (2.0 * PI) + time * 0.04, 0.2) : unpackColor(v_TexIndex);
    color = mix(color, vec3(1.0), 0.45 * exp(-r / 0.35));

    writeLight(color * (1.6 * rays * rise * fade + glow) * breath * alpha);
}
