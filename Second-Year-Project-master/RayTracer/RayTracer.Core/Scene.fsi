namespace RayTracer.Core
open RayTracer.Shapes.Shape
open RayTracer.Entities
open RayTracer.Entities.AmbientLight
open RayTracer.Entities.Light
open RayTracer.KDTree.SceneTree

module Scene = 

    type Scene 

    val mkScene     : shapes : Shape[] ->  max_reflect : int -> lights : Light list -> AmbientLight -> Scene
    val getTree     : Scene -> SceneTree
    val getLights   : Scene -> Light list
    val getAmbient  : Scene -> AmbientLight
    val getReflect  : Scene -> int

