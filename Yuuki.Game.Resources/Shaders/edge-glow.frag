#version 450

// The light round the screen's edges, as an arcade cabinet's. On its vertices:
//   v_TexCoords  how far in from the nearest side (x) and the nearest top or bottom (y), in its reaches
//   v_Color      the light's color (linear), and how strong it is at the edge
//   v_TexIndex   how fast it falls away: how many times over it fades by e across a reach

#include "yuuki.glsl"

// One edge's light at a distance in from it: brightest at the edge, falling away smoothly as light leaking in does.
float edgeLight(float distance, float falloff)
{
    return exp(-distance * falloff);
}

void main()
{
    float falloff = v_TexIndex;
    float side = edgeLight(v_TexCoords.x, falloff);
    float end = edgeLight(v_TexCoords.y, falloff);

    // Where two edges' light meets it adds up as light does on a screen: a corner is no brighter than an edge, and
    // there's no crease where one edge's light gives way to the other's.
    float light = side + end - side * end;

    // A little whiter where it's strongest.
    vec3 color = mix(v_Color.rgb, vec3(1.0), 0.2 * light * light);
    writeLight(color * light * v_Color.a);
}
