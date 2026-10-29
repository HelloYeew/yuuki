#version 450

// Sunlight out from behind a rank that has just landed, as through leaves and petals: shafts of every width, broken
// into streaks, with hair-thin rays between, bunched here and spaced there. Nothing moves round: each ray brightens,
// dims and flickers where it is, as leaves moving in front of the sun would make it. It's never a round shape (a
// strong ray reaches far, a faint one stops short) and it leans up and to the left, where the light comes from (the
// bubble's glints are on that side too), in a soft glow round the sun. On its vertices, the same on each but
// v_TexCoords:
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

    // The rays: noise read round a circle, so they join up all the way round, with time as its third dimension, so
    // each ray changes where it is. Broad shafts broken into streaks, thinner rays between, and hair-thin ones.
    float circle = density / (2.0 * PI);
    float broad = noise(vec3(around * circle, time * 0.22));
    float fine = noise(vec3(around * circle * 3.1 + 7.0, time * 0.37));
    float hair = noise(vec3(around * circle * 7.3 + 19.0, time * 0.5));
    float rays = pow(broad, narrowness) * (0.5 + 0.5 * fine) + 0.35 * pow(fine, narrowness * 1.6) + 0.2 * pow(hair, 3.0);

    // A shimmer where each ray is, as leaves moving in front of the sun.
    rays *= 0.7 + 0.3 * noise(vec3(around * circle * 1.5 + 40.0, time * 1.3));

    // Where the light comes from: up and to the left. How much this way faces it, -1 to 1.
    float lean = dot(around, vec2(-0.6, -0.8));

    // How far the light reaches this way: a strong ray far, a faint one a little way, further toward the light.
    float reach = min((0.52 + 0.48 * max(broad, 0.85 * fine)) * (0.88 + 0.12 * lean), 0.98);

    // Out from the middle: rising out of the glow, then dying away to nothing at its reach.
    float rise = smoothstep(0.02, 0.25, r);
    // It thins away gradually toward its reach rather than ending in a blunt tip.
    float along = r / reach;
    float fade = exp(-2.5 * along * along) * (1.0 - smoothstep(0.6, 1.0, along));

    // The glow round the sun, leaning toward the light too, and the slow breathing of it all.
    float glowReach = 0.5 * (0.85 + 0.2 * lean);
    float glow = 0.35 * exp(-pow(r / glowReach, 2.0)) * pow(max(1.0 - r, 0.0), 1.4);
    float breath = 0.88 + 0.12 * noise(vec2(time * 0.35, 3.1));

    // White hot close in, its own color further out.
    vec3 color = heroes ? heroColor(angle / (2.0 * PI) + time * 0.04, 0.2) : unpackColor(v_TexIndex);
    color = mix(color, vec3(1.0), 0.45 * exp(-r / 0.35));

    writeLight(color * (1.7 * rays * rise * fade * (0.85 + 0.15 * lean) + glow) * breath * alpha);
}
