#version 450

// The front of a bubble's film, over whatever is inside the bubble: its rim, the bright line at its edge, its glints
// and the glow round it.

#include "bubble.glsl"

void main()
{
    drawFilm(true);
}
