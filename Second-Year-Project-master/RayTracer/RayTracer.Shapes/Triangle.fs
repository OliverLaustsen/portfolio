namespace RayTracer.Shapes
open RayTracer.Shapes
open RayTracer.Entities
open RayTracer.Helpers    
open Shape
open Ray
open MathHelpers
open EquationHelpers
open Point
open Texture
open BoundingBox
open Transformation
open BaseShape

module Triangle =

    let getBoundingBox a b c =  
                    let minX = min (Point.getX c) (min (Point.getX a) (Point.getX b))
                    let minY = min (Point.getY c) (min (Point.getY a) (Point.getY b))
                    let minZ = min (Point.getZ c) (min (Point.getZ a) (Point.getZ b))
                    let minP = mkPoint minX minY minZ

                    let maxX = max (Point.getX c) (max (Point.getX a) (Point.getX b))
                    let maxY = max (Point.getY c) (max (Point.getY a) (Point.getY b))
                    let maxZ = max (Point.getZ c) (max (Point.getZ a) (Point.getZ b))
                    let maxP = mkPoint maxX maxY maxZ

                    let e = 0.000001
                    mkBoundingBox (minP--e) (maxP++e)

    let hitFunctionSmooth (av:Vertice) (bv:Vertice) (cv:Vertice) ray (tex:Texture) (smooth:bool) uVector vVector= 
        let a = av.Point
        let b = bv.Point
        let c = cv.Point

        let ax = Point.getX a
        let ay = Point.getY a
        let az = Point.getZ a
        let bx = Point.getX b
        let by = Point.getY b
        let bz = Point.getZ b
        let cx = Point.getX c
        let cy = Point.getY c
        let cz = Point.getZ c
                
        let r = Ray.getVector ray
        let dx = Vector.getX r
        let dy = Vector.getY r
        let dz = Vector.getZ r
        let rp = Ray.getPoint ray
        let ox = Point.getX rp 
        let oy = Point.getY rp
        let oz = Point.getZ rp

        let D = (ax-bx)*(((ay-cy)*dz)-(dy*(az-cz))) 
                    + (ax-cx)*((dy*(az-bz))-((ay-by)*dz))
                    + dx*(((ay-by)*(az-cz))-((ay-cy)*(az-bz)))

        let div = 1.0/D

        let beta = ((ax-ox)*(((ay-cy)*dz)-(dy*(az-cz))) 
                        + (ax-cx)*((dy*(az-oz))-((ay-oy)*dz))
                        + dx*(((ay-oy)*(az-cz))-((ay-cy)*(az-oz))))
                        * div
        let gamma = ((ax-bx)*(((ay-oy)*dz)-(dy*(az-oz)))
                        + (ax-ox)*((dy*(az-bz))-((ay-by)*dz))
                        + dx*(((ay-by)*(az-oz))-((ay-oy)*(az-bz))))
                        * div
        let t = ((ax-bx)*(((ay-cy)*(az-oz))-((ay-oy)*(az-cz)))
                        + (ax-cx)*(((ay-oy)*(az-bz))-((ay-by)*(az-oz)))
                        + (ax-ox)*(((ay-by)*(az-cz))-((ay-cy)*(az-bz))))
                        * div


        if (beta >=< (0.0,1.0) && gamma >=< (0.0,1.0) && (beta+gamma) >=< (0.0,1.0) && t > 0.0) then
            let alpha = 1.0-beta-gamma

            let mutable normal =
                if smooth then
                    //Smooth shading
                    let v = alpha * av.Normal + beta * bv.Normal + gamma * cv.Normal
                    Vector.normalise v
                else
                    //non-smooth shading
                    Vector.normalise (Vector.crossProduct  vVector uVector)

            let u = alpha * (fst av.TextureCoordinate) + beta * (fst bv.TextureCoordinate) + gamma * (fst cv.TextureCoordinate)
            let v = alpha * (snd av.TextureCoordinate) + beta * (snd bv.TextureCoordinate) + gamma * (snd cv.TextureCoordinate)

            let material = getMaterial tex v u
            Some(Hit.mkHit t normal material)
        else
            None


    //Makes triangle
    let mkTriangle a b c m smooth = 
        let va = {new Vertice with
                      member this.Point = a
                      member this.Normal = Vector.mkVector 0.0 0.0 1.0
                      member this.TextureCoordinate = (0.0,0.0) }
        let vb = {new Vertice with
                      member this.Point = b
                      member this.Normal = Vector.mkVector 0.0 0.0 1.0
                      member this.TextureCoordinate = (0.0,0.0) }
        let vc = {new Vertice with
                      member this.Point = c
                      member this.Normal = Vector.mkVector 0.0 0.0 1.0
                      member this.TextureCoordinate = (0.0,0.0) }
        let uVector = Point.distance va.Point vb.Point
        let vVector = Point.distance va.Point vc.Point
        {   
        new Shape with 
            member this.Texture = m
            member this.Inside = None
            member this.BoundingBox = getBoundingBox a b c
            member this.TransformedBoundingBox = this.BoundingBox
            member this.Hit (ray:Ray) = hitFunctionSmooth va vb vc ray this.Texture smooth uVector vVector
        }

    let mkPlyTriangle (va:Vertice) (vb:Vertice) (vc:Vertice) smooth = 
        let uVector = Point.distance va.Point vb.Point
        let vVector = Point.distance va.Point vc.Point
        {   new BaseShape 
            with member this.BoundingBox = getBoundingBox va.Point vb.Point vc.Point
                 member this.TransformedBoundingBox = this.BoundingBox
                 member this.Hit = (fun ray tex -> hitFunctionSmooth va vb vc ray tex smooth uVector vVector)
                 member this.Inside = None
        }