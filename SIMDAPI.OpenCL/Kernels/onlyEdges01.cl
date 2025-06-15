__kernel void onlyEdges01(
    __global const uchar* inputPixels,
    __global uchar* outputPixels,
    const int width,
    const int height,
    float threshold,
    const int thickness,
    const int edgeR,
    const int edgeG,
    const int edgeB)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    const int pixelPos = (y * width + x) * 4;

    // Clamp all color values to 0-255 (jetzt mit getauschten R/B-Kanälen)
    const uchar clampedB = (uchar)min(max(edgeR, 0), 255);  // R-Wert geht auf B-Kanal
    const uchar clampedG = (uchar)min(max(edgeG, 0), 255);
    const uchar clampedR = (uchar)min(max(edgeB, 0), 255);  // B-Wert geht auf R-Kanal
    const int clampedThickness = min(max(thickness, 0), 10);
    const float absThreshold = fabs(threshold);

    // Weißer Hintergrund (unverändert)
    outputPixels[pixelPos]     = 255;  // R
    outputPixels[pixelPos + 1] = 255;  // G
    outputPixels[pixelPos + 2] = 255;  // B
    outputPixels[pixelPos + 3] = 255;  // A

    // Nur Nicht-Randpixel verarbeiten
    if (x >= clampedThickness && x < width - clampedThickness &&
        y >= clampedThickness && y < height - clampedThickness)
    {
        // Sobel Kernel (unverändert)
        const int sobelX[3][3] = {{-1, 0, 1}, {-2, 0, 2}, {-1, 0, 1}};
        const int sobelY[3][3] = {{-1, -2, -1}, {0, 0, 0}, {1, 2, 1}};

        float3 gradientX = (float3)(0.0f);
        float3 gradientY = (float3)(0.0f);

        // 3x3 Nachbarschaft verarbeiten (mit getauschten Kanälen)
        for (int dy = -1; dy <= 1; dy++) {
            for (int dx = -1; dx <= 1; dx++) {
                int neighborPos = ((y + dy) * width + (x + dx)) * 4;
                
                // Hier werden R und B getauscht für die Gradientenberechnung
                float3 rgb = {
                    inputPixels[neighborPos + 2] / 255.0f,  // B -> R
                    inputPixels[neighborPos + 1] / 255.0f,  // G bleibt
                    inputPixels[neighborPos]     / 255.0f   // R -> B
                };

                gradientX += rgb * sobelX[dy + 1][dx + 1];
                gradientY += rgb * sobelY[dy + 1][dx + 1];
            }
        }

        // Gradientenmagnitude berechnen
        float3 magnitude = sqrt(gradientX * gradientX + gradientY * gradientY);
        float avgMagnitude = (magnitude.x + magnitude.y + magnitude.z) / 3.0f;

        // Kanten zeichnen wenn über Schwellwert
        if (avgMagnitude > absThreshold) {
            for (int dy = -clampedThickness; dy <= clampedThickness; dy++) {
                for (int dx = -clampedThickness; dx <= clampedThickness; dx++) {
                    if (dx*dx + dy*dy <= clampedThickness*clampedThickness) {
                        int px = x + dx;
                        int py = y + dy;
                        if (px >= 0 && px < width && py >= 0 && py < height) {
                            int outPos = (py * width + px) * 4;
                            // Hier werden R und B in der Ausgabe getauscht
                            outputPixels[outPos]     = clampedR;  // Original B
                            outputPixels[outPos + 1] = clampedG;
                            outputPixels[outPos + 2] = clampedB;  // Original R
                            outputPixels[outPos + 3] = 255;
                        }
                    }
                }
            }
        }
    }
}