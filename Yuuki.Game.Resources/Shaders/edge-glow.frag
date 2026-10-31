#version 450

// The light round the screen's edges, as an arcade cabinet's. On its vertices, v_TexCoords says how far in from the
// nearest side (x) and the nearest top or bottom (y), in its reaches.

#include "yuuki.glsl"

layout(set = 0, binding = 5, std140) uniform EdgeGlowBlock
{
    vec4 u_Color; // The light's color (linear), and how strong it is at the edge.
};

// How fast it falls away: how many times over it fades by e across a reach.
const float FALLOFF = 2.5;

// One edge's light at a distance in from it: brightest at the edge, falling away smoothly as light leaking in does.
float edgeLight(float distance, float falloff)
{
    return exp(-distance * falloff);
}

void main()
{
    float side = edgeLight(v_TexCoords.x, FALLOFF);
    float end = edgeLight(v_TexCoords.y, FALLOFF);

    // Where two edges' light meets it adds up as light does on a screen: a corner is no brighter than an edge, and
    // there's no crease where one edge's light gives way to the other's.
    float light = side + end - side * end;

    // A little whiter where it's strongest.
    vec3 color = mix(u_Color.rgb, vec3(1.0), 0.2 * light * light);
    writeLight(color * light * u_Color.a);
}
