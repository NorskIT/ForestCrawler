using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestCrawler;

// Bounded local fallback for rock surfaces omitted or displaced by the game's navmesh.
// Every graph edge uses the same production support and body-clearance checks as movement.
internal static class SurfacePath
{
    private sealed class Node
    {
        internal int X, Z;
        internal Vector3 Point;
        internal float Cost, Score;
        internal Node? Parent;
        internal bool Closed;
    }
    internal static bool TryBuild(Vector3 from, Vector3 target, float radius,
        ApproachPath.GroundQuery ground, Func<Vector3,Vector3,bool> segment,
        Func<Vector3,bool> goalClear, List<Vector3> result)
    {
        result.Clear();
        if (Vector3.Distance(from,target)>24) return false;
        const float spacing=.5f;
        const int budget=2048;
        var nodes=new Dictionary<(int,int),Node>();
        var open=new List<Node>();
        var start=new Node { Point=from, Score=Vector3.Distance(from,target) };
        nodes[(0,0)]=start; open.Add(start);
        float minX=Mathf.Min(from.x,target.x)-8, maxX=Mathf.Max(from.x,target.x)+8;
        float minZ=Mathf.Min(from.z,target.z)-8, maxZ=Mathf.Max(from.z,target.z)+8;
        int visited=0;
        while(open.Count>0 && visited++<budget)
        {
            int best=0;
            for(int i=1;i<open.Count;i++) if(open[i].Score<open[best].Score) best=i;
            var node=open[best]; open.RemoveAt(best); node.Closed=true;
            if(Vector3.Distance(node.Point,target)<=radius && goalClear(node.Point))
            {
                for(Node? n=node;n!=null;n=n.Parent) result.Add(n.Point);
                result.Reverse(); return result.Count>=2;
            }
            for(int dx=-1;dx<=1;dx++) for(int dz=-1;dz<=1;dz++)
            {
                if(dx==0 && dz==0) continue;
                int x=node.X+dx,z=node.Z+dz;
                var probe=new Vector3(from.x+x*spacing,node.Point.y,from.z+z*spacing);
                if(probe.x<minX || probe.x>maxX || probe.z<minZ || probe.z>maxZ) continue;
                if(nodes.TryGetValue((x,z),out var neighbor) && neighbor.Closed) continue;
                if(!ground(probe,out var point) || !segment(node.Point,point)) continue;
                float cost=node.Cost+Vector3.Distance(node.Point,point);
                if(neighbor!=null && cost>=neighbor.Cost) continue;
                if(neighbor==null)
                {
                    neighbor=new Node { X=x,Z=z }; nodes[(x,z)]=neighbor; open.Add(neighbor);
                }
                neighbor.Point=point; neighbor.Cost=cost; neighbor.Parent=node;
                neighbor.Score=cost+Mathf.Max(0,Vector3.Distance(point,target)-radius);
            }
        }
        return false;
    }
}
