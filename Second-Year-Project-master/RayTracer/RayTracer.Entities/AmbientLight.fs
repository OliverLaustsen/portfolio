namespace RayTracer.Entities
open RayTracer.Entities.Colour

module AmbientLight = 


    type AmbientLight = 
     | AmbientLight of Colour * float

    let mkAmbientLight colour brightness = AmbientLight(colour,brightness)
    let getColour (AmbientLight(colour,_)) = colour
    let getBrightness (AmbientLight(_,brightness)) = brightness