namespace RayTracer.Shapes

open RayTracer.Helpers.MathHelpers
open RayTracer.Helpers.EquationHelpers
open RayTracer.Entities
open RayTracer.Entities.Material
open RayTracer.Entities.Vector
open RayTracer.Entities.Ray
open RayTracer.Entities.Texture
open RayTracer.Entities.BoundingBox
open RayTracer.Entities.Point
open RayTracer.Entities.Transformation
open RayTracer.Expressions
open RayTracer.Expressions.ExprToPoly
open RayTracer.Expressions.ExprParse
open Hit
open Colour
open BaseShape
open Bounded

module Shape = 
    exception InvalidExpression

    type Shape = 
        inherit Bounded
        //Colour of the Shape
        abstract member Texture : Texture

        //Polynomium of the Shape
        //abstract member Poly : poly

        //Inside function for shapes
        abstract member Inside : (Point -> bool) option

        //Hit function of the Shape
        abstract member Hit : Ray -> Hit option

        //Texture of the Shape
        //abstract member Texture : texture

    //Types for triangleMeshes
    type Vertice = 
        abstract member Point : Point
        abstract member Normal : Vector
        abstract member TextureCoordinate : TextureCoordinate
    
    type TriangleMesh = 
        inherit Shape
        abstract member Vertices : Map<int,Vertice>
        abstract member Triangles : Map<int,Vertice*Vertice*Vertice>

    //Makes a vertice from a point
    let mkVertice x y z nx ny nz u v = {   new Vertice with
        member this.Point = mkPoint x y z   
        member this.Normal = mkVector nx ny nz
        member this.TextureCoordinate = u,v
    }

    let isInsideBox L H p = 
        let lx = Point.getX L
        let ly = Point.getY L
        let lz = Point.getZ L

        let hx = Point.getX H
        let hy = Point.getY H
        let hz = Point.getZ H

        let ox = Point.getX p
        let oy = Point.getY p
        let oz = Point.getZ p

        let x = (lx < ox) && (hx > ox)
        let y = (ly < oy) && (hy > oy)
        let z = (lz < oz) && (hz > oz)

        if x && y && z then true else false

    let isInsideSphere C R (p:Point) =
        let cx = Point.getX C
        let cy = Point.getY C
        let cz = Point.getZ C

        let ox = Point.getX p
        let oy = Point.getY p
        let oz = Point.getZ p

        let xd = pown (ox) 2
        let yd = pown (oy) 2
        let zd = pown (oz) 2
        let d = sqrt(xd+yd+zd)
        if d <= R+0.000001 then true else false

    let isInsideCylinder c r h p =
        let cx = Point.getX c
        let cy = Point.getY c
        let cz = Point.getZ c

        let px = Point.getX p
        let py = Point.getY p
        let pz = Point.getZ p

        let xd = (px)**2.0
        let yd = (py)**2.0
        let zd = (pz)**2.0

        let heightPos = (h/2.0) + cy
        let heightNeg = -(h/2.0) + cy
        if py < heightPos && py > heightNeg then
            let d = sqrt (xd+zd)
            if d <= r+0.000001 then 
                true 
            else false
        else false

        

    let mkVertice3 x y z = mkVertice x y z 0.0 0.0 0.0 0.0 0.0
    let mkVertice5 x y z u v = mkVertice x y z 0.0 0.0 0.0 u v
    let mkVertice6 x y z nx ny nz = mkVertice x y z nx ny nz 0.0 0.0

    let mkVerticeTypes p n t = {   new Vertice with 
        member this.Point = p   
        member this.Normal = n
        member this.TextureCoordinate = t
    }

    let mkTransformed (shape:Shape) ((transform:Transformation),(inverse:Transformation)) = 
        {   new Shape
            with member this.Texture = shape.Texture
                 member this.Inside = Some (fun (p:Point) -> 
                                            let newP = transformPoint p inverse.Matrix
                                            match shape.Inside with
                                            |Some b -> b newP
                                            |None -> false)
                                       
                 member this.BoundingBox = shape.BoundingBox
                 member this.TransformedBoundingBox = transformBoundingbox shape.TransformedBoundingBox transform
                 member this.Hit (ray:Ray) =
                    let p = Ray.getPoint ray
                    let tp = transformPoint p inverse.Matrix
            
                    let d = Ray.getVector ray
                    let td = transformVector d inverse.Matrix
                
                    let ray = mkRay td tp
                    
                    let hit = shape.Hit ray

                    match hit with 
                    | Some (hit) -> Some (mkHit (Hit.getT hit) (transformVector (Hit.getNormal hit) transform.Matrix) (Hit.getMaterial hit))
                    | None -> None
        }

    //Makes sphere
    let mkSphere t r p = 
        {   new Shape 
            with member this.Texture = t
                 member this.Inside = Some (isInsideSphere p r)
                 member this.BoundingBox = 
                    let lp = mkPoint -r -r -r
                    let hp = mkPoint r r r
                    mkBoundingBox lp hp
                 member this.TransformedBoundingBox = this.BoundingBox
                 member this.Hit (ray:Ray) =
                    let o = Ray.getPoint ray
                    let ox = Point.getX o
                    let oy = Point.getY o 
                    let oz = Point.getZ o

                    let d = Ray.getVector ray
                    let dx = Vector.getX d
                    let dy = Vector.getY d
                    let dz = Vector.getZ d

                    let a = dx**2.0 + dy**2.0 + dz**2.0
                    let b = 2.0*((ox*dx) + (oy*dy) + (oz*dz))
                    let c = ox**2.0 + oy**2.0 + oz**2.0 - (r**2.0)
                
                    match RayTracer.Helpers.EquationHelpers.solveSecondDegree a b c with 
                    None -> None
                    | Some (x1,x2) -> 
                        match posMin x1 x2 with
                        Some(t) -> 
                            let point = Ray.getPosition ray t
                            let pointAsVector = Vector.mkVector (Point.getX point) (Point.getY point) (Point.getZ point)
                            let normal = (1.0/r) * pointAsVector

                            let lon = let phi' = System.Math.Atan2 ((Vector.getX normal), (Vector.getZ normal))
                                      if phi' < 0.0 then phi' + 2.0 * System.Math.PI
                                      else phi' 
                            let lat = System.Math.Acos (Vector.getY normal)

                            let u = lon / (2.0 * System.Math.PI)
                            let v = 1.0 - (lat / System.Math.PI)
                            
                            let material = Texture.getMaterial this.Texture u v
                            Some (mkHit t normal material)
                        | None -> None
                               
                                
        }

    //Makes rectangle
    let mkRectangle tex bl tl br =
        let width = Vector.magnitude (Point.distance bl br)
        let height = Vector.magnitude (Point.distance bl tl)
 
        let u = Point.direction bl br
        let v = Point.direction bl tl
        let w = Vector.normalise(Vector.crossProduct u v)
 
        let ux = Vector.getX u
        let uy = Vector.getY u
        let uz = Vector.getZ u
 
        let vx = Vector.getX v
        let vy = Vector.getY v
        let vz = Vector.getZ v
 
        let wx = Vector.getX w
        let wy = Vector.getY w
        let wz = Vector.getZ w
 
        let blx = Point.getX bl
        let bly = Point.getY bl
        let blz = Point.getZ bl
 
        let o = mkTransformation (array2D [|[|ux;vx;wx;0.0|];[|uy;vy;wy;0.0|];[|uz;vz;wz;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseo = mkTransformation (array2D [|[|ux;uy;uz;0.0|];[|vx;vy;vz;0.0|];[|wx;wy;wz;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let translate = translate blx bly blz
 
        let rect = {   new Shape
                       with member this.Texture = tex
                            member this.Inside = None
                            member this.BoundingBox =
                                let lp = mkPoint 0.0 0.0 0.0
                                let hp = mkPoint width height 0.0 
                                mkBoundingBox lp hp
                            member this.TransformedBoundingBox = this.BoundingBox
                            member this.Hit (ray:Ray) =
                               
                                let ox = Point.getX (Ray.getPoint ray)
                                let oy = Point.getY (Ray.getPoint ray)
                                let oz = Point.getZ (Ray.getPoint ray)
 
                               
                                let dx = Vector.getX (Ray.getVector ray)
                                let dy = Vector.getY (Ray.getVector ray)
                                let dz = Vector.getZ (Ray.getVector ray)
                                if dz = 0.0 then
                                    None
                                else
                                    let t = (0.0 - oz)/dz
                                    if t < 0.0 then None else
                                    let px = ox + t * dx
                                    let py = oy + t * dy
                                   
                                    let normal = mkVector 0.0 0.0 1.0
 
                                    if (px >= 0.0 && px <= width) then
                                        if (py >= 0.0 && py <= height) then
                                            let u = px/width
                                            let v = py/height
                                            let material = Texture.getMaterial this.Texture u v
                                            Some (mkHit t normal material)
                                        else None
                                    else None
                    }
        let ortho = mkTransformed rect (o,inverseo)
        mkTransformed ortho translate

    //Makes disk
    let mkDisk tex r =
        {   new Shape
            with member this.Texture = tex
                 member this.Inside = None
                 member this.BoundingBox = 
                    let lp = mkPoint -r -r 0.0
                    let hp = mkPoint r r 0.0
                    mkBoundingBox lp hp
                 member this.TransformedBoundingBox = this.BoundingBox
                 member this.Hit (ray:Ray) =
                    let o = Ray.getPoint ray
                    let ox = Point.getX o
                    let oy = Point.getY o
                    let oz = Point.getZ o
                
                    let d = Ray.getVector ray
                    let dx = Vector.getX d
                    let dy = Vector.getY d
                    let dz = Vector.getZ d

                    if dz = 0.0 then None else 
                    let t = -(oz/dz)
                    if t < 0.0 then None else
                    let px = ox + t * dx
                    let py = oy + t * dy

                    let normal = Vector.mkVector 0.0 0.0 1.0
                    if ((px**2.0 + py**2.0) <= r**2.0) then 
                        let u = (px + r)/(2.0 * r)
                        let v = (py + r)/(2.0 * r)
                        let material = Texture.getMaterial this.Texture u v
                        Some (mkHit t normal material) 
                    else None 
        }

    //Makes plane
    let mkPlane t =
        {   new Shape
            with member this.Texture = t
                 member this.Inside = None
                 member this.BoundingBox = mkBoundingBox (mkPoint -10000.0 -10000.0 -10000.0) (mkPoint 10000.0 10000.0 10000.0)
                 member this.TransformedBoundingBox = this.BoundingBox
                 member this.Hit (ray:Ray) =
                    let o = Ray.getPoint ray
                    let ox = Point.getX o
                    let oy = Point.getY o
                    let oz = Point.getZ o
                
                    let d = Ray.getVector ray
                    let dx = Vector.getX d
                    let dy = Vector.getY d
                    let dz = Vector.getZ d
                    

                    if dz = 0.0 then None else 
                    let t = -(oz / dz)
                    if t <= 0.0 then None else
                    let normal = Vector.mkVector 0.0 0.0 1.0
                    let px = ox + t * dx 
                    let py = oy + t * dy
                    //Change when implementing texture
                    let material = Texture.getMaterial this.Texture px py
                    Some (mkHit t normal material)
        }

    //Makes a shape from a BaseShape
    let mkShape (shape:BaseShape) t = 
        {   new Shape 
            with member this.Texture = t
                 member this.Inside = shape.Inside
                 member this.BoundingBox =  shape.BoundingBox
                 member this.TransformedBoundingBox = this.BoundingBox
                 member this.Hit (ray:Ray) = shape.Hit ray t
        }
    
    //Makes open cylinder
    let mkOpenCylinder t r h = 
        {   new Shape 
            with member this.Texture = t
                 member this.Inside = None
                 member this.BoundingBox = 
                    let lp = mkPoint -r -h -r
                    let hp = mkPoint r h r
                    mkBoundingBox lp hp
                 member this.TransformedBoundingBox = this.BoundingBox
                 member this.Hit (ray:Ray) =
                    let min = -(h/2.0)
                    let max =  h/2.0

                    let o = Ray.getPoint ray
                    let ox = Point.getX o
                    let oy = Point.getY o 
                    let oz = Point.getZ o

                    let d = Ray.getVector ray
                    let dx = Vector.getX d
                    let dy = Vector.getY d
                    let dz = Vector.getZ d

                    let a = (pown dx 2) + (pown dz 2)

                    let b = 2.0 * (ox * dx + oz * dz)

                    let c = pown ox 2 + pown oz 2 - pown r 2

                    let getResult (t:float) (p:Point) = 
                        let normal = Vector.mkVector (Point.getX p / r) 0.0 (Point.getZ p / r)
                        
                        let lon = let phi' = System.Math.Atan2 ((Vector.getX normal), (Vector.getZ normal))
                                  if phi' < 0.0 then phi' + 2.0 * System.Math.PI
                                  else phi' 
                        let lat = System.Math.Acos (Vector.getY normal)

                        let u = lon / (2.0 * System.Math.PI)
                        let v = ((Point.getY p) / h) + 0.5
                        //Change when implementing texture
                        let material = Texture.getMaterial this.Texture u v
                        Some(mkHit t normal material)

                    match RayTracer.Helpers.EquationHelpers.solveSecondDegree a b c with 
                    None -> None 
                    | Some (t1,t2) -> let p1 = Ray.getPosition ray t1
                                      let p2 = Ray.getPosition ray t2

                                      let p1y = Point.getY p1
                                      let p2y = Point.getY p2

                                      if(p1y >=< (min,max) && p2y >=< (min,max)) then 
                                            match posMin t1 t2 with 
                                              Some(t) when t = t1 -> getResult t1 p1
                                            | Some(t) when t = t2 -> getResult t2 p2
                                            | None -> None
                                            | (_) -> failwith "expected a number"
                                      else if(p1y >=< (min,max) && t1 > 0.0) then getResult t1 p1
                                      else if(p2y >=< (min,max) && t2 > 0.0) then getResult t2 p2
                                      else None
        }

    let mkClosedCylinder midt topt bott r h (c:Point) = 
        let cyl = mkOpenCylinder midt r h
        let topTransform = mergeTransformations [rotateX -(System.Math.PI/2.0);translate 0.0 (h/2.0) 0.0]
        let botTransform = mergeTransformations [rotateX (System.Math.PI/2.0);translate 0.0 -(h/2.0) 0.0]
        let top = mkTransformed (mkDisk topt r) (topTransform)
        let bot = mkTransformed (mkDisk bott r) (botTransform)
        let parts = [cyl;top;bot]
        {   new Shape 
            with member this.Texture = midt
                 member this.Inside = Some (isInsideCylinder c r h)
                 member this.BoundingBox =                     
                    let lp = mkPoint -r -(h/2.0) -r
                    let hp = mkPoint r (h/2.0) r
                    mkBoundingBox lp hp
                 member this.TransformedBoundingBox = this.BoundingBox
                 member this.Hit (ray:Ray) =

                    let mutable closestHit = Hit.mkEmptyHit

                    for shape in parts do 
                        let hit = shape.Hit ray
                        match hit with
                        | Some hit -> if Hit.getT closestHit = 0.0 || (Hit.getT hit) < (Hit.getT closestHit) then 
                                         closestHit <- hit
                        | None -> ()
                    if (Hit.getT closestHit) = 0.0 then None else Some(closestHit)

            
    }   

    let mkBox L H one two three four five six =
        {   new Shape
            with member this.Texture = one
                 member this.Inside = Some (isInsideBox L H)
                 member this.BoundingBox = mkBoundingBox L H
                 member this.TransformedBoundingBox = this.BoundingBox
                 member this.Hit ray =
                    let o = Ray.getPoint ray
                    let ox = Point.getX o
                    let oy = Point.getY o 
                    let oz = Point.getZ o

                    let d = Ray.getVector ray
                    let dx = Vector.getX d
                    let dy = Vector.getY d
                    let dz = Vector.getZ d

                    let lx = Point.getX L
                    let ly = Point.getY L
                    let lz = Point.getZ L

                    let hx = Point.getX H
                    let hy = Point.getY H
                    let hz = Point.getZ H

                    let ttx = findTbox dx ox lx hx
                    let tty = findTbox dy oy ly hy
                    let ttz = findTbox dz oz lz hz

                    let tx = fst ttx
                    let ty = fst tty
                    let tz = fst ttz
                    let t'x = snd ttx
                    let t'y = snd tty
                    let t'z = snd ttz
                    
                    let t = max (max tx ty) tz
                    let t' = min (min t'x t'y) t'z
                    //Change when implementing texture
                    let material = Texture.getMaterial this.Texture 0.0 0.0
                    if t < t' && t' > 0.0 then
                                                if t > 0.0 then
                                                    (match t with
                                                    |s when s = tx -> if dx > 0.0 then
                                                                          let normal = mkVector -1.0 0.0 0.0
                                                                          let u = (oz+t*dz - lz)/(hz-lz)
                                                                          let v = (oy+t*dy - ly)/(hy-ly)
                                                                          let mat = Texture.getMaterial five v u
                                                                          Some(mkHit t normal mat)
                                                                      else 
                                                                          let normal = mkVector 1.0 0.0 0.0
                                                                          let u = (oz+t*dz - hz)/(lz-hz)
                                                                          let v = (oy+t*dy - hy)/(ly-hy)
                                                                          let mat = Texture.getMaterial six (1.0 - v) (1.0 - u) 
                                                                          Some(mkHit t normal mat)
                                                    |s when s = ty -> if dy > 0.0 then
                                                                          let normal = mkVector 0.0 -1.0 0.0
                                                                          let u = (oz+t*dz - lz)/(hz-lz)
                                                                          let v = (ox+t*dx - lx)/(hx-lx)
                                                                          let mat = Texture.getMaterial four v u
                                                                          Some(mkHit t normal mat)
                                                                      else 
                                                                          let normal = mkVector 0.0 1.0 0.0
                                                                          let u = (oz+t*dz - hz)/(lz-hz)
                                                                          let v = (ox+t*dx - hx)/(lx-hx)
                                                                          let mat = Texture.getMaterial three (1.0 - v) (1.0 - u) 
                                                                          Some(mkHit t normal mat)
                                                    |s when s = tz -> if dz > 0.0 then
                                                                          let normal = mkVector 0.0 0.0 1.0
                                                                          let u = (ox+t*dx - lx)/(hx-lx)
                                                                          let v = (oy+t*dy - ly)/(hy-ly)
                                                                          let mat = Texture.getMaterial two u v
                                                                          Some(mkHit t normal mat)
                                                                      else
                                                                          let normal = mkVector 0.0 0.0 -1.0
                                                                          let u = (ox+t*dx - hx)/(lx-hx)
                                                                          let v = (oy+t*dy - hy)/(ly-hy)
                                                                          let mat = Texture.getMaterial one (1.0 - u) (1.0 - v)
                                                                          Some(mkHit t normal mat)
                                                    | (_) -> failwith "expected something else")

                                                else
                                                    (match t' with
                                                    |s when s = t'x -> if dx < 0.0 then
                                                                          let normal = mkVector 1.0 0.0 0.0
                                                                          let u = (oz+t'*dz - hz)/(lz-hz)
                                                                          let v = (oy+t'*dy - hy)/(ly-hy)
                                                                          let mat = Texture.getMaterial two u v
                                                                          Some(mkHit t' normal mat)
                                                                        else 
                                                                          let normal = mkVector -1.0 0.0 0.0
                                                                          let u = (oz+t'*dz - lz)/(hz-lz)
                                                                          let v = (oy+t'*dy - ly)/(hy-ly)
                                                                          let mat = Texture.getMaterial five v u
                                                                          Some(mkHit t' normal mat)
                                                  
                                                    |s when s = t'z -> if dz > 0.0 then
                                                                          let normal = mkVector 0.0 0.0 1.0
                                                                          let u = (ox+t'*dx - lx)/(hx-lx)
                                                                          let v = (oy+t'*dy - ly)/(hy-ly)
                                                                          let mat = Texture.getMaterial six u v
                                                                          Some(mkHit t' normal mat)
                                                                       else
                                                                          let normal = mkVector 0.0 0.0 -1.0
                                                                          let u = (ox+t'*dx - hx)/(lx-hx)
                                                                          let v = (oy+t'*dy - hy)/(ly-hy)
                                                                          let mat = Texture.getMaterial one (1.0 - u) (1.0 - v)
                                                                          Some(mkHit t' normal mat)
                                                    |s when s = t'y -> if dy < 0.0 then
                                                                            let normal = mkVector 0.0 -1.0 0.0
                                                                            let u = (oz+t'*dz - lz)/(hz-lz)
                                                                            let v = (ox+t'*dx - lx)/(hx-lx)
                                                                            let mat = Texture.getMaterial four u v
                                                                            Some(mkHit t' normal mat)
                                                                       else 
                                                                            let normal = mkVector 0.0 1.0 0.0
                                                                            let u = (oz+t'*dz - hz)/(lz-hz)
                                                                            let v = (ox+t'*dx - hx)/(lx-hx)
                                                                            let mat = Texture.getMaterial three (1.0 - v) (1.0 - u) 
                                                                            Some(mkHit t' normal mat)
                                                    | (_) -> failwith "expected something else")
                    else None
        }

    exception UnexpectedNoneValue
    exception DeterminePointInside
    exception UnknownTValue

    let rec refireRay (ray:Ray) (s1:Shape) (s2:Shape) =
        let s1hitOption = s1.Hit ray
        let s2hitOption = s2.Hit ray
        match s1hitOption with
        | Some s1hit -> match s2hitOption with
                        | Some(s2hit) -> let s1T = Hit.getT s1hit
                                         let s2T = Hit.getT s2hit
                                         let s1HitPoint = Ray.getPosition ray s1T
                                         let s2HitPoint = Ray.getPosition ray s2T
                                         let smallT = min s1T s2T
                                         if smallT = s1T then 
                                             let s1HitIsInsideS2 = match s2.Inside with
                                                                     | Some(b) -> b s1HitPoint
                                                                     | None -> false
                                             if s1HitIsInsideS2 then
                                                 let firePoint = Point.move s1HitPoint (Vector.multScalar 0.00001 (Ray.getVector ray))
                                                 let newray = Ray.mkRay (Ray.getVector ray) firePoint
                                                 match refireRay newray s1 s2 with
                                                 | Some(refireHit) -> Some(mkHit ((Hit.getT refireHit)+s1T) (Hit.getNormal refireHit) (Hit.getMaterial refireHit))
                                                 | None -> None
                                             else s1hitOption
                                         else 
                                             let s2HitIsInsideS1 = match s1.Inside with
                                                                     | Some(b) -> b s2HitPoint
                                                                     | None -> false
                                             if s2HitIsInsideS1 then 
                                                let firePoint = Point.move s2HitPoint (Vector.multScalar 0.00001 (Ray.getVector ray))
                                                let newray = Ray.mkRay (Ray.getVector ray) firePoint
                                                match refireRay newray s1 s2 with
                                                | Some(refireHit) -> Some(mkHit ((Hit.getT refireHit)+s2T) (Hit.getNormal refireHit) (Hit.getMaterial refireHit))
                                                | None -> None
                                             else s2hitOption
                        | None -> s1hitOption
        | None -> s2hitOption

    let rec refireRayIntersect (ray:Ray) (s1:Shape) (s2:Shape) =
        let s1hitOption = s1.Hit ray
        let s2hitOption = s2.Hit ray
        match s1hitOption with
        | Some s1hit -> match s2hitOption with
                        | Some(s2hit) -> let s1T = Hit.getT s1hit
                                         let s2T = Hit.getT s2hit
                                         let s1HitPoint = Ray.getPosition ray s1T
                                         let s2HitPoint = Ray.getPosition ray s2T
                                         let smallT = min s1T s2T
                                         if smallT = s1T then 
                                             let s1HitIsInsideS2 = match s2.Inside with
                                                                   | Some(b) -> b s1HitPoint
                                                                   | None -> false
                                             if s1HitIsInsideS2 then s1hitOption else
                                               let firePoint = Point.move s1HitPoint (Vector.multScalar 0.00001 (Ray.getVector ray))
                                               let newray = Ray.mkRay (Ray.getVector ray) firePoint
                                               match refireRayIntersect newray s1 s2 with
                                               | Some(refireHit) -> Some(mkHit ((Hit.getT refireHit)+s1T) (Hit.getNormal refireHit) (Hit.getMaterial refireHit))
                                               | None -> None
                                          else 
                                             let s2HitIsInsideS1 = match s1.Inside with
                                                                   | Some(b) -> b s2HitPoint
                                                                   | None -> false
                                             if s2HitIsInsideS1 then s2hitOption else
                                               let firePoint = Point.move s2HitPoint (Vector.multScalar 0.00001 (Ray.getVector ray))
                                               let newray = Ray.mkRay (Ray.getVector ray) firePoint
                                               match refireRayIntersect newray s1 s2 with
                                               | Some(refireHit) -> Some(mkHit ((Hit.getT refireHit)+s2T) (Hit.getNormal refireHit) (Hit.getMaterial refireHit))
                                               | None -> None
                        | None -> None
        | None -> None

    let rec refireRaySubtraction (ray:Ray) (s1:Shape) (s2:Shape) =
        let rpos = Ray.getPoint ray
        let rposIsInsideS2 = match s2.Inside with
                                | Some(b) -> b rpos
                                | None -> false
        let returner = if rposIsInsideS2 then
                        let s2hitOption = s2.Hit ray
                        match s2hitOption with
                        |Some hit -> 
                                    let s2T = Hit.getT hit
                                    if s2T >= 0.0 then 
                                        let s2HitPoint = Ray.getPosition ray (Hit.getT hit)
                                        let s2HitIsInsideS1 = match s1.Inside with
                                                                | Some(b) -> b s2HitPoint
                                                                | None -> false
                                        if s2HitIsInsideS1 then s2hitOption else
                                                            let firePoint = Point.move s2HitPoint (Vector.multScalar 0.00001 (Ray.getVector ray))
                                                            let newray = Ray.mkRay (Ray.getVector ray) firePoint
                                                            let s1hitOption = s1.Hit newray 
                                                            match s1hitOption with
                                                            |Some b -> 
                                                                    let s1HitPoint = Ray.getPosition ray (Hit.getT b)
                                                                    let firePoint = Point.move s1HitPoint (Vector.multScalar 0.00001 (Ray.getVector ray))
                                                                    let newray = Ray.mkRay (Ray.getVector ray) firePoint
                                                                    match refireRaySubtraction newray s1 s2 with
                                                                    | Some(refireHit) -> Some(mkHit ((Hit.getT refireHit)+(Hit.getT b)) (Hit.getNormal refireHit) (Hit.getMaterial refireHit))
                                                                    | None -> None
                                                            |None -> None
                                    else None
                        |None -> None
                        else 
                        let s1hitOption = s1.Hit ray
                        match s1hitOption with
                        |Some b -> 
                                    let s1T = Hit.getT b
                                    if s1T < 0.0 then None
                                    else
                                        let s1HitPoint = Ray.getPosition ray s1T
                                        let s1HitIsInsideS2 = match s2.Inside with
                                                                |Some(b) -> b s1HitPoint
                                                                |None -> false
                                        if not s1HitIsInsideS2 then s1hitOption else
                                            let firePoint = Point.move s1HitPoint (Vector.multScalar 0.00001 (Ray.getVector ray))
                                            let newray = Ray.mkRay (Ray.getVector ray) firePoint
                                            let s2hitOption = s2.Hit newray
                                            match s2hitOption with
                                            |Some hit -> let s2HitPoint = Ray.getPosition newray (Hit.getT hit)
                                                         let s2HitIsInsideS1 = match s1.Inside with
                                                                               | Some(b) -> b s2HitPoint
                                                                               | None -> false
                                                         if s2HitIsInsideS1 then Some(mkHit ((Hit.getT hit)+s1T) (Hit.getNormal hit) (Hit.getMaterial hit)) else
                                                                             match refireRaySubtraction newray s1 s2 with
                                                                             | Some(refireHit) -> Some(mkHit ((Hit.getT refireHit)+s1T) (Hit.getNormal refireHit) (Hit.getMaterial refireHit))
                                                                             | None -> None
                                            |None -> None

                        |None -> None
                                
        returner

    let mkUnion (s1:Shape) (s2:Shape) = 
        {   new Shape
            with member this.Texture = s1.Texture
                 member this.Inside = 
                     Some ( fun p -> 
                        let in1 =
                            match s1.Inside with
                            | Some(b) -> b p
                            | None -> false

                        let in2 =
                            match s2.Inside with
                            | Some(b) -> b p
                            | None -> false

                        (in1 || in2)
                     )
                 member this.BoundingBox = BoundingBox.getUnion s1.BoundingBox s2.BoundingBox
                 member this.TransformedBoundingBox = BoundingBox.getUnion s1.TransformedBoundingBox s2.TransformedBoundingBox
                 member this.Hit (ray:Ray) = 
                    refireRay ray s1 s2

        }

    let mkIntersect (s1:Shape) (s2:Shape) = 
        {   new Shape
            with member this.Texture = s1.Texture
                 member this.Inside = 
                     Some ( fun p -> 
                        let in1 = Option.map (fun ins -> ins p ) s1.Inside
                        let in2 = Option.map (fun ins -> ins p ) s2.Inside

                        let in1 =
                            match in1 with
                            | Some(b) -> b
                            | None -> false

                        let in2 =
                            match in2 with
                            | Some(b) -> b
                            | None -> false

                        in1 && in2 
                     )
                 member this.BoundingBox = s1.BoundingBox
                 member this.TransformedBoundingBox = s1.TransformedBoundingBox
                 member this.Hit (ray:Ray) = 
                    refireRayIntersect ray s1 s2
       }
        
    let mkSubtraction (s1:Shape) (s2:Shape) =
      {   new Shape
          with member this.Texture = s1.Texture
               member this.Inside = 
                     Some ( fun p -> 
                        let in1 = Option.map (fun ins -> ins p ) s1.Inside
                        let in2 = Option.map (fun ins -> ins p ) s2.Inside

                        let in1 =
                            match in1 with
                            | Some(b) -> b
                            | None -> false

                        let in2 =
                            match in2 with
                            | Some(b) -> b
                            | None -> false
                        //Change below TODO
                        in1 && not in2 
                     )
               member this.BoundingBox = s1.BoundingBox
               member this.TransformedBoundingBox = s1.TransformedBoundingBox
               member this.Hit (ray:Ray) =
                refireRaySubtraction ray s1 s2
        }   
    let mkGroup (s1:Shape) (s2:Shape) =
      {   new Shape 
          with member this.Texture = s1.Texture
               member this.Inside = 
                     Some ( fun p -> 
                        let in1 =
                            match s1.Inside with
                            | Some(b) -> b p
                            | None -> false

                        let in2 =
                            match s2.Inside with
                            | Some(b) -> b p
                            | None -> false

                        (in1 || in2)
                     )
               member this.BoundingBox = BoundingBox.getUnion s1.BoundingBox s2.BoundingBox
               member this.TransformedBoundingBox = BoundingBox.getUnion s1.TransformedBoundingBox s2.TransformedBoundingBox
               member this.Hit ray = 
                let s1Hit = s1.Hit ray
                let s2Hit = s2.Hit ray
                if s1Hit.IsNone && s2Hit.IsNone then
                        None
                    else 
                        if s1Hit.IsNone then 
                            s2Hit
                        elif s2Hit.IsNone then
                            s1Hit
                        else
                            let hit1 = 
                                match s1Hit with
                                    | None          -> raise UnexpectedNoneValue
                                    | Some hit   -> hit
                            let hit2 = 
                                match s2Hit with
                                    | None          -> raise UnexpectedNoneValue
                                    | Some hit   -> hit
                            let smallT = if (Hit.getT hit1) > (Hit.getT hit2) then (Hit.getT hit2) else (Hit.getT hit1)
                            if smallT = (Hit.getT hit1) then
                                    s1Hit
                            elif smallT = (Hit.getT hit2) then
                                    s2Hit
                            else
                                raise UnknownTValue
       }
                    