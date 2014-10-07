using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CIT_Util
{
    public static class Extensions
    {
        public static float[] GetRgba(this Color color)
        {
            var ret = new float[4];
            ret[0] = color.r;
            ret[1] = color.g;
            ret[2] = color.b;
            ret[3] = color.a;
            return ret;
        }

        public static bool IsMouseOverRect(this Rect windowRect)
        {
            var mousePosFromEvent = Event.current.mousePosition;
            return windowRect.Contains(mousePosFromEvent);
        }

        public static Color MakeColorTransparent(this Color color, float transparency)
        {
            var rgba = color.GetRgba();
            return new Color(rgba[0], rgba[1], rgba[2], transparency);
        }

        public static Part PartFromHit(this RaycastHit hit)
        {
            if (hit.collider == null || hit.collider.gameObject == null)
            {
                return null;
            }
            var go = hit.collider.gameObject;
            var p = Part.FromGO(go);
            while (p == null)
            {
                if (go.transform != null && go.transform.parent != null && go.transform.parent.gameObject != null)
                {
                    go = go.transform.parent.gameObject;
                }
                else
                {
                    break;
                }
                p = Part.FromGO(go);
            }
            return p;
        }

        public static void RecursePartList(this Part part, ICollection<Part> list)
        {
            list.Add(part);
            foreach (var p in part.children)
            {
                p.RecursePartList(list);
            }
        }

        public static double GetMassOfPartAndChildren(this Part part)
        {
            if (part == null)
            {
                return 0d;
            }
            double sum = part.mass;
            sum += part.children.Sum(pc => pc.GetMassOfPartAndChildren());
            return sum;
        }
    }
}