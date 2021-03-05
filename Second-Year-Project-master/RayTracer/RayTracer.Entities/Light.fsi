namespace RayTracer.Entities
open RayTracer.Entities.Vector
open RayTracer.Entities.Point
open RayTracer.Entities.Colour

module Light = 

    type Light
    
    val mkLight : position:Point -> colour:Colour -> float -> Light
    val getPos : Light -> Point
    val getColour : Light -> Colour
    val getBrightness : Light -> float