﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media.Media3D;
using FreelancerModStudio.Data;
using HelixEngine;

namespace FreelancerModStudio.SystemPresenter
{
    public abstract class ContentBase : ITableRow<int>
    {
        public int ID { get; set; }

        public ModelVisual3D Model { get; set; }
        public Matrix3D Matrix { get; set; }

        public string Title { get; set; }
        public bool Visibility { get; set; }

        public void SetDisplay(Matrix3D matrix)
        {
            if (Model != null)
            {
                ContentAnimation animation = new ContentAnimation()
                {
                    OldMatrix = Matrix,
                    NewMatrix = matrix,
                };
                Animator.Animate(Model, animation);
            }

            Matrix = matrix;
        }

        public void SetDisplay(Vector3D position, Vector3D rotation, Vector3D scale)
        {
            SetDisplay(GetMatrix(position, rotation, scale));
        }

        Matrix3D GetMatrix(Vector3D position, Vector3D rotation, Vector3D scale)
        {
            Matrix3D matrix = new Matrix3D();

            matrix.Scale(scale);

            matrix.Rotate(new Quaternion(new Vector3D(1, 0, 0), rotation.X));
            matrix.Rotate(new Quaternion(new Vector3D(0, 1, 0) * matrix, rotation.Y));
            matrix.Rotate(new Quaternion(new Vector3D(0, 0, 1) * matrix, rotation.Z));

            matrix.Translate(position);

            return matrix;
        }

        public ContentBase()
        {
            Matrix = Matrix3D.Identity;

            Visibility = true;
        }

        protected abstract GeometryModel3D GetGeometry();
        public abstract MeshGeometry3D GetMesh();

        public void LoadModel()
        {
            Model = new ModelVisual3D() { Content = GetGeometry() };
            SetDisplay(Matrix);
        }

        public Vector3D GetPosition()
        {
            return new Vector3D(Matrix.OffsetX, Matrix.OffsetY, Matrix.OffsetZ);
        }
    }
}
