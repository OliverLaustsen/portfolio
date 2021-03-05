namespace RayTracer.Entities
open Material
open System.Drawing
open Colour

module Texture = 
    type TextureCoordinate = float * float

    type Texture = 
    | Texture of (float -> float -> Material)

    let mkTexture (f:float -> float -> Material) = Texture f

    let mkMatTexture mat = mkTexture (fun x y -> mat)

    let getFunc (Texture(f)) = f

    let mkTextureFromFile (tr : float -> float -> float * float) (file : string) =
        let img = new Bitmap(file)
        let width = img.Width - 1
        let height = img.Height - 1
        let widthf = float width
        let heightf = float height
        let texture x y =
          let (x', y') = tr x y
          let x'', y'' = int (widthf * x'), int (heightf * y')
          let c = img.GetPixel(x'',y'')
          mkMaterial (fromColor c) 0.0
        mkTexture texture

    let getMaterial text x y = (getFunc text) x y