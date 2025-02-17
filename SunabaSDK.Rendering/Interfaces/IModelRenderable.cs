﻿using SunabaSDK.Rendering.Renderables;
using SunabaSDK.Rendering.Resources;
using System.Numerics;

namespace SunabaSDK.Rendering.Interfaces
{
    public interface IModelRenderable : IRenderable, IUpdateable, IResource
    {
        IModel Model { get; }
        Vector3 Origin { get; set; }
        Vector3 Angles { get; set; }
        int Sequence { get; set; }

        Matrix4x4 GetModelTransformation();
        (Vector3, Vector3) GetBoundingBox();
    }
}