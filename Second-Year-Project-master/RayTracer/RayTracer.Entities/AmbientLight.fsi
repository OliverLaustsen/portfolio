namespace RayTracer.Entities
open RayTracer.Entities.Vector
open RayTracer.Entities.Point
open RayTracer.Entities.Colour

module AmbientLight = 

    type AmbientLight
    
    val mkAmbientLight : colour:Colour -> float -> AmbientLight
    val getColour : AmbientLight -> Colour
    val getBrightness : AmbientLight -> float