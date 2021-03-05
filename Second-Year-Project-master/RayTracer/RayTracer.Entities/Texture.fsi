namespace RayTracer.Entities
open Material
module Texture = 
    type TextureCoordinate = float * float
    type Texture = 
    | Texture of (float -> float -> Material)

    val mkTexture : (float -> float -> Material) -> Texture

    val mkMatTexture : Material -> Texture

    val mkTextureFromFile : (float -> float -> float * float) -> string -> Texture

    val getMaterial : Texture -> float -> float -> Material