namespace RayTracer.Entities
module Material = 
    open RayTracer.Entities.Colour

    type Material

    val mkMaterial : Colour -> float -> Material
    val mkMaterialFromTuple : (Colour * float) -> Material
    val getColour : Material -> Colour
    val getReflect : Material -> float