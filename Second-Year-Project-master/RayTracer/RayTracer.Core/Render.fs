namespace RayTracer.Core

open Scene
open Camera
open RayTracer.Shapes
open RayTracer.Entities
open RayTracer.Entities.Colour
open Material
open System.Threading.Tasks
open Vector
open Hit
open System.Drawing
open RayTracer.KDTree.KDTree
open RayTracer.KDTree.Traversal


module Render = 

    let getHit scene ray =
        let shapes = Scene.getTree scene
        let hit = traverseScene shapes ray
        let mutable returnHit = mkEmptyHit
        
        match hit with
            | Some (hit2) -> if (Hit.getT returnHit = 0.0 || Hit.getT hit2 < Hit.getT returnHit)then
                                let norm = Hit.getNormal hit2
                                if ((Vector.dotProduct norm (Ray.getVector ray))) > 0.0 then
                                    let hit3 = mkHit (Hit.getT hit2) -norm (Hit.getMaterial hit2)
                                    returnHit <- hit3
                                else
                                    returnHit <- hit2
            | None -> ()  
               
        if Hit.getT returnHit = 0.0 then 
            None 
        else 
            Some(returnHit)       

    let getLight scene r hit = 
        let mutable startColour = Material.getColour (Hit.getMaterial hit)
        let mutable accum = mkColour 0.0 0.0 0.0
        let hitPoint = Point.move(Ray.getPosition r (Hit.getT hit)) (Vector.multScalar 0.000001 (Hit.getNormal hit))

        for light in Scene.getLights scene do
            let ray = Ray.mkRay (Point.direction hitPoint (Light.getPos light)) hitPoint
            let lightRayHit = getHit scene ray
            match lightRayHit with
            | Some (hit2) -> if (Hit.getT hit2) > Vector.magnitude (Point.distance hitPoint (Light.getPos light)) then 
                                  let c = Vector.dotProduct (Hit.getNormal hit) (Point.direction hitPoint (Light.getPos light))
                                  if c > 0.0 then 
                                      let i = Light.getBrightness light
                                      let col = (Light.getColour light)
                                      let resultCol = (c * i) * col
                                      accum <- (accum + resultCol)
                              else 
                                  ()
            | None -> let mutable c = (Vector.dotProduct (Hit.getNormal hit) (Point.direction hitPoint (Light.getPos light)))
                      if c > 0.0 then 
                          let i = Light.getBrightness light
                          let col = (Light.getColour light)
                          let resultCol = (c * i) * col
                          accum <- (accum + resultCol)

        accum <- accum + (AmbientLight.getBrightness (Scene.getAmbient scene) * AmbientLight.getColour (Scene.getAmbient scene))
        startColour * accum 
      
    let rec bounceRay scene r hit i = 
        let hitOnObject = Point.move (Ray.getPosition r (Hit.getT hit)) (Vector.multScalar 0.000001 (Hit.getNormal hit))
        let rayFromObject = Ray.mkRay (Vector.normalise ((Ray.getVector r) - (2.0 * (Vector.dotProduct (Hit.getNormal hit) (Ray.getVector r)) * (Hit.getNormal hit)))) hitOnObject
        let bounceHit = if Material.getReflect (Hit.getMaterial hit) > 0.0 then 
                            match getHit scene rayFromObject with
                            | Some nextHit ->
                                if i > 0 then
                                    Colour.merge (Material.getReflect (Hit.getMaterial hit)) (bounceRay scene rayFromObject nextHit (i-1)) (getLight scene r hit) 
                                else 
                                    (getLight scene rayFromObject nextHit)
                            | None -> Colour.merge (Material.getReflect (Hit.getMaterial hit)) (mkColour 0.0 0.0 0.0) (getLight scene r hit) 
                        else 
                            (getLight scene r hit)
        bounceHit

    let calcColour scene ray =
        let cameraHit = getHit scene ray

        match cameraHit with
        | Some(hit) -> let hit = if (Hit.getT hit) = -infinity then
                                    mkEmptyHit
                                 else 
                                    hit
                       
                       let bounceCol = if Material.getReflect (Hit.getMaterial hit) > 0.0 && (Scene.getReflect scene) > 0 then
                                           (bounceRay scene ray hit ((Scene.getReflect scene)-1))
                                       else
                                           (getLight scene ray hit)
                       bounceCol 
        | None -> mkColour 0.0 0.0 0.0

            

    let render scene camera = 
        let pixelWidth = (getUnitX camera)/(float)(getResWidth camera)
        let pixelHeight = (getUnitY camera)/(float)(getResHeight camera)

        let wVector = Vector.normalise (Point.distance (getLookAt camera) (getPos camera))
        let uVector = Vector.normalise (Vector.crossProduct (getUpVector camera) wVector)
        let vVector = Vector.crossProduct uVector wVector

        let calcRay (x:int) (y:int) = 
            let px = pixelWidth * (float (x - ((getResWidth camera) / 2)) + 0.5)
            let py = pixelHeight * (float (y - ((getResHeight camera) / 2)) + 0.5)

            let direction = Vector.normalise (px * uVector + py * vVector - (getZoom camera) * wVector )

            Ray.mkRay direction (getPos camera)

        let image = new Bitmap (getResWidth camera, getResHeight camera)
        let resW  = getResWidth camera
        let resH = getResHeight camera
        let array = Array2D.init resW resH (fun x y -> Color.Black)

        Parallel.For(0,(getResWidth camera), (fun x -> 
            for y = 0 to (resH - 1) do
                let ray = calcRay x y
                let colour = calcColour scene ray
                Array2D.set array x y (Colour.toColor colour)
        ))
        |> ignore
        
        for x = 0 to (resW - 1) do
            for y = 0 to (resH - 1) do
                image.SetPixel(x,y,array.[x,y])
        
        image



    let renderToFile scene camera path = 
        let img = render scene camera
        img.Save path      
                         
    let renderToScreen scene camera = 
        let img = render scene camera 
        img.Save "output.png"
        ignore (System.Diagnostics.Process.Start("cmd", "/c output.png"))
        ()