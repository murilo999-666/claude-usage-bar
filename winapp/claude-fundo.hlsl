// Fundo animado do terminal do Claude.
// - fade-in suave nos primeiros instantes da janela
// - dois brilhos (terracota e lilás) que passeiam devagar pelo fundo
// - o efeito some perto das bordas para casar com a margem da janela
// Só mexe nos pixels que são "fundo"; o texto fica intacto.

Texture2D shaderTexture;
SamplerState samplerState;

cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

float brilho(float2 uv, float2 centro, float raio)
{
    float d = length((uv - centro) * float2(Resolution.x / Resolution.y, 1.0));
    return exp(-(d * d) / (raio * raio));
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float4 cor = shaderTexture.Sample(samplerState, tex);
    float t = Time * 0.06;

    // centros que se movem em órbitas lentas e diferentes
    float2 c1 = float2(0.18 + 0.10 * sin(t * 1.3), 0.15 + 0.08 * cos(t * 1.1));
    float2 c2 = float2(0.85 + 0.08 * cos(t * 0.9), 0.88 + 0.07 * sin(t * 1.4));

    float3 terracota = float3(0.851, 0.467, 0.341);
    float3 lilas     = float3(0.545, 0.447, 0.800);

    float3 fundo = Background.rgb;
    fundo += terracota * 0.085 * brilho(tex, c1, 0.55);
    fundo += lilas     * 0.070 * brilho(tex, c2, 0.50);

    // A margem interna da janela fica fora do shader e tem a cor pura do fundo.
    // Então o efeito vai sumindo perto da borda, para não marcar um retângulo.
    float bx = 60.0 / Resolution.x, by = 60.0 / Resolution.y;
    float borda = smoothstep(0.0, bx, tex.x) * smoothstep(0.0, bx, 1.0 - tex.x)
                * smoothstep(0.0, by, tex.y) * smoothstep(0.0, by, 1.0 - tex.y);
    fundo = Background.rgb + (fundo - Background.rgb) * borda;

    // quanto este pixel é "fundo" (texto e bordas de letra ficam como estão)
    float dif = length(cor.rgb - Background.rgb);
    float eFundo = 1.0 - saturate(dif * 6.0);
    float3 saida = cor.rgb + (fundo - Background.rgb) * eFundo;

    // fade-in de abertura (0,7 s) com leve subida de brilho
    float abre = smoothstep(0.0, 0.7, Time);
    saida *= abre;

    return float4(saida, cor.a);
}
