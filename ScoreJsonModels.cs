using System;

[Serializable]
public record TimeSigJson(
    int numerator,
    int denominator,
    int barIndex
);

[Serializable]
public record KeySigJson(
    int key,
    int type,
    int barIndex
);

[Serializable]
public record TempoChangeJson(
    double bpm,
    int barIndex
);
