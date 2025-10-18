﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Threading.Tasks;

class Pro
{
    static void ProV()
    {


        string inputPath = "object.obj";
        string outputPath = "objectAO.png";
        int raysPerVertex = 512;
        int size = 2048;

        Console.WriteLine($"Loading {inputPath}...");
        var mesh = ObjLoader.Load(inputPath);
        Console.WriteLine($"Loaded {mesh.Vertices.Count} vertices, {mesh.Faces.Count} faces");

        var aoValues = BakeMap.ComputeAO(mesh, raysPerVertex);
        var aoMap = BakeMap.BakeToTexture(mesh, aoValues, size);
        aoMap.Save(outputPath);

        //Console.WriteLine($"Saving AO map to {outputPath}");
       


        Console.WriteLine("Computing curvature...");
        var curvValues = BakeMap.ComputeCurvature(mesh);
        var curvMap = BakeMap.BakeToTexture(mesh, curvValues, size);
        curvMap.Save("objectCurvature.png");


        Console.WriteLine("Done!");
    }
}






