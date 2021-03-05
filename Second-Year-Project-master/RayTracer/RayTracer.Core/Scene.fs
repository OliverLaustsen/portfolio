namespace RayTracer.Core
open RayTracer.Shapes.Shape
open RayTracer.Entities.Light
open Camera
open RayTracer.Entities.AmbientLight
open RayTracer.KDTree.KDTree
open RayTracer.KDTree.SceneTree
open RayTracer.KDTree.Construction

module Scene = 

    type Scene = | Scene of SceneTree * int * Light list * AmbientLight

    let mkScene shapes reflect light ambientlight = 
        let tree = mkSceneTree shapes
        Scene(tree,reflect,light,ambientlight)
    let getTree (Scene(tree,_,_,_)) = tree
    let getReflect (Scene(_,reflect,_,_)) = reflect
    let getLights (Scene(_,_,light,_)) = light
    let getAmbient (Scene(_,_,_,ambient)) = ambient

    