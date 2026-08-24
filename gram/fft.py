"""Small dependency-free real FFT helper for the terminal visualizer."""

from __future__ import annotations

import cmath
import math


def magnitudes(samples: list[float], bins: int = 24) -> list[float]:
    """Return logarithmically useful magnitudes using a radix-2 FFT."""
    size = 1
    while size < len(samples):
        size <<= 1
    values = [complex(samples[i] if i < len(samples) else 0.0, 0) for i in range(size)]
    j = 0
    for i in range(1, size):
        bit = size >> 1
        while j & bit:
            j ^= bit
            bit >>= 1
        j ^= bit
        if i < j:
            values[i], values[j] = values[j], values[i]
    length = 2
    while length <= size:
        root = cmath.exp(-2j * math.pi / length)
        for start in range(0, size, length):
            factor = 1 + 0j
            half = length // 2
            for i in range(start, start + half):
                even = values[i]
                odd = factor * values[i + half]
                values[i], values[i + half] = even + odd, even - odd
                factor *= root
        length <<= 1
    raw = [abs(value) / size for value in values[: size // 2]]
    group = max(1, len(raw) // bins)
    result = []
    for index in range(bins):
        section = raw[index * group : (index + 1) * group]
        value = max(section, default=0.0)
        result.append(min(1.0, math.sqrt(value * 18)))
    return result
