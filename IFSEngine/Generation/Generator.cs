﻿using IFSEngine.Model;
using IFSEngine.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace IFSEngine.Generation;

public class Generator
{
    public List<Transform> SelectedTransforms { get; set; }

    private static readonly HashSet<string> _preferredTransformNames = new() { "Affine", "Möbius", "Rotate Euler", "Spherical", "Translate" };
    private static readonly HashSet<string> _angleParams = new() { "angle", "rot", "rotate", "rotation", "orientation", "inclination", "azimuth" };

    public Generator(IEnumerable<Transform> transforms)
    {
        SelectedTransforms = transforms.ToList();
    }

    public IEnumerable<IFS> GenerateBatch(GeneratorOptions options)
    {
        for (int i = 0; i < options.BatchSize; i++)
        {
            yield return GenerateOne(options);
        }
    }

    public IFS GenerateOne(GeneratorOptions options)
    {
        IFS gen = options.baseParams.DeepClone();
        var preferredTranforms = SelectedTransforms.Where(t => _preferredTransformNames.Contains(t.Name)).ToList();
        if (options.MutateIterators)
        {
            while (gen.Iterators.Count < 4)
            {
                gen.AddIterator(CreateIterator(options, preferredTranforms), true);
            }
            if (options.MutationChance > RandHelper.NextDouble())
            {
                gen.AddIterator(CreateRandomOrPreferredIterator(options), true);
            }
            if (gen.Iterators.Count > 4 && options.MutationChance > RandHelper.NextDouble())
            {
                var iter = gen.Iterators.ElementAt(RandHelper.Next(gen.Iterators.Count));
                gen.RemoveIterator(iter);
            }
        }
        if (options.MutateParameters)
        {
            foreach (var iterator in gen.Iterators)
                MutateIteratorParams(iterator, options);
        }
        if (options.MutateConnections)
        {//add/remove
            foreach (var it in gen.Iterators)
            {
                foreach (var itTo in gen.Iterators)
                {
                    if (!it.WeightTo.TryGetValue(itTo, out _))
                        it.WeightTo[itTo] = 0.0;//hack
                    if (options.MutationChance*0.5 > RandHelper.NextDouble()) //TODO: separate chance?
                    {
                        it.WeightTo[itTo] = 1.0 - (it.WeightTo[itTo] > 0.0 ? 1.0 : 0.0);
                    }
                }
            }
        }
        if (options.MutateConnectionWeights)
        {
            foreach (var it in gen.Iterators)
            {
                foreach (var itTo in gen.Iterators)
                {
                    if (it.WeightTo[itTo] == 0.0)
                        continue;
                    it.WeightTo[itTo] = Math.Max(0, MutateValue(it.WeightTo[itTo], options.MutationChance, options.MutationStrength));
                }
            }
        }
        if (options.MutatePalette)
        {
            //TODO: params
            var bias = RandomVector(0.4f, 0.8f);
            var mult = RandomVector(0.2f, 1.2f);
            var freq = RandomVector(0.1f, 1.0f);
            var phase = RandomVector(0.0f, 1.0f);
            gen.Palette = PaletteFromIqParams(bias, mult, freq, phase);
        }
        if (options.MutateColoring)
        {
            foreach (var it in gen.Iterators)
            {
                it.ColorIndex = Math.Clamp(MutateValue(it.ColorIndex, options.MutationChance, options.MutationStrength), 0.0, 1.0);
                it.ColorSpeed = MutateValue(it.ColorSpeed, options.MutationChance, options.MutationStrength);
            }
        }
        return gen;
    }

    private Iterator CreateRandomOrPreferredIterator(GeneratorOptions options)
    {
        Iterator newIterator;
        //50% chance the new iterator is preferred
        if (RandHelper.NextDouble() < 0.5)
            newIterator = CreateIterator(options, SelectedTransforms);
        else
        {
            var preferredTranforms = SelectedTransforms.Where(t => _preferredTransformNames.Contains(t.Name)).ToList();
            newIterator = CreateIterator(options, preferredTranforms);
        }
        return newIterator;
    }

    private static Iterator CreateIterator(GeneratorOptions options, List<Transform> transforms)
    {
        var selectedTransform = transforms[RandHelper.Next(transforms.Count)]; 

        var iterator = new Iterator(selectedTransform)
        {
            BaseWeight = 0.5 + RandHelper.NextDouble(),
            StartWeight = 1.0,
            ColorIndex = RandHelper.NextDouble(),
            ColorSpeed = 0.25 + 0.5*RandHelper.NextDouble(),
            Opacity = (RandHelper.Next(3) == 0) ? 0 : RandHelper.NextDouble(),
            ShadingMode = (RandHelper.Next(10) == 0) ? ShadingMode.DeltaPSpeed : ShadingMode.Default
        };

        //consider tags to use plugins the right way
        if (iterator.Transform.Tags.Contains("shape"))
        {
            iterator.Opacity = 0.0;
            iterator.Add = 1.0;
            iterator.ColorSpeed = 0.0;
        }

        MutateIteratorParams(iterator, options);
        return iterator;
    }

    private static double MutateValue(double val, double chance, double strength)
    {
        if (chance > RandHelper.NextDouble())
            return val + -strength + 2.0 * strength * RandHelper.NextDouble();
        else
            return val;
    }
    private static Vector3 MutateVec3(Vector3 val, double chance, double strength)
    {
        if (chance > RandHelper.NextDouble())
        {
            Vector3 v = val;
            v.X = (float)(v.X + -strength + 2.0 * strength * RandHelper.NextDouble());
            v.Y = (float)(v.Y + -strength + 2.0 * strength * RandHelper.NextDouble());
            v.Z = (float)(v.Z + -strength + 2.0 * strength * RandHelper.NextDouble());
            return v;
        }
        else
            return val;
    }
    private static void MutateIteratorParams(Iterator iterator, GeneratorOptions options)
    {
        foreach (var v in iterator.RealParams)
        {
            iterator.RealParams[v.Key] = MutateValue(iterator.RealParams[v.Key], options.MutationChance, options.MutationStrength * (IsAngleParameter(v.Key) ? 360 : 1));
        }
        foreach (var v in iterator.Vec3Params)
        {
            iterator.Vec3Params[v.Key] = MutateVec3(iterator.Vec3Params[v.Key], options.MutationChance, options.MutationStrength * (IsAngleParameter(v.Key) ? 360 : 1));
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// Based on this article from Inigo Quilez: <a href="https://iquilezles.org/www/articles/palettes/palettes.htm">iquilezles.org</a>
    /// </remarks>
    /// <returns></returns>
    private static FlamePalette PaletteFromIqParams(Vector4 bias, Vector4 mult, Vector4 freq, Vector4 phase)
    {
        List<Vector4> colors = new List<Vector4>();

        for (float t = 0; t < 1.0; t += 0.1f)
        {//could be based on freq
            Vector4 c = bias + mult * new Vector4(
                (float)Math.Cos(2 * Math.PI * (t * freq.X + phase.X)),
                (float)Math.Cos(2 * Math.PI * (t * freq.Y + phase.Y)),
                (float)Math.Cos(2 * Math.PI * (t * freq.Z + phase.Z)),
                1.0f);
            c = Vector4.Clamp(c, Vector4.Zero, Vector4.One);
            colors.Add(HsvToRgb(c));
        }

        return new FlamePalette
        {
            Name = "Generated Palette",
            Colors = colors
        };
    }

    private static Vector4 HueToRgb(float hue)
    {
        double R = Math.Abs(hue * 6 - 3) - 1;
        double G = 2 - Math.Abs(hue * 6 - 2);
        double B = 2 - Math.Abs(hue * 6 - 4);
        R = Math.Clamp(R, 0.0, 1.0);
        G = Math.Clamp(G, 0.0, 1.0);
        B = Math.Clamp(B, 0.0, 1.0);
        return new Vector4((float)R, (float)G, (float)B, 1.0f);
    }
    private static Vector4 HsvToRgb(Vector4 hsv)
    {
        Vector4 rgb = HueToRgb(hsv.X);
        return ((rgb - Vector4.One) * hsv.Y + Vector4.One) * hsv.Z;
    }

    private static Vector4 RandomVector(float min, float max)
    {
        float range = max - min;
        return new Vector4(
            min + range * (float)RandHelper.NextDouble(),
            min + range * (float)RandHelper.NextDouble(),
            min + range * (float)RandHelper.NextDouble(),
            1.0f);
    }

    private static bool IsAngleParameter(string paramName)
    {
        var lc = paramName.ToLowerInvariant();
        return lc == "r" || _angleParams.Any(p => lc.Contains(p));
    }

}
