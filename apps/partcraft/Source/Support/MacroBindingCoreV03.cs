using System;
using System.Linq;
using System.Collections.Generic;

// Macro traversal order, with linear native-ID guards instead of pairwise COM identity.
// Delegates let tests exercise the actual traversal/cache logic without a COM server.
public static class MacroBindingCoreV03 {
    public static List<T> Collect<T>(T first,Func<T,T> nextRoot,Func<T,T> child,Func<T,T> nextChild,Func<T,int> id,int limit) where T:class {
        var result=new List<T>();var visited=new HashSet<int>();int visits=0;
        Action<T,bool,int> chain=null;
        chain=(node,sub,depth)=>{
            if(depth>512)throw new InvalidOperationException("Feature nesting safety limit");
            var siblings=new HashSet<int>();
            while(node!=null){
                if(++visits>limit*10)throw new InvalidOperationException("Feature reference safety limit");
                int key=id(node);if(!siblings.Add(key))throw new InvalidOperationException("Native feature sibling cycle: "+key);
                if(visited.Add(key)){
                    if(result.Count>=limit)throw new InvalidOperationException("Feature count safety limit");
                    result.Add(node);chain(child(node),true,depth+1);
                }
                node=sub?nextChild(node):nextRoot(node);
            }
        };
        chain(first,false,0);return result;
    }
    public static T UniqueCached<T>(IEnumerable<T> cache,Func<T,bool> same,string name) {
        var matches=cache.Where(same).ToArray();
        if(matches.Length!=1)throw new InvalidOperationException("Native cached Cut List membership count="+matches.Length+" for "+name);
        return matches[0];
    }
    sealed class Node {public int id;public Node next,child;}
    public static int Tests(){int count=0;Action<string,Action> test=(name,run)=>{run();count++;Console.WriteLine(name+": PASS");};
        Func<Node,List<Node>> collect=first=>Collect(first,n=>n.next,n=>n.child,n=>n.next,n=>n.id,100);
        test("Macro traversal nested children",()=>{var a=new Node{id=1,child=new Node{id=2}};var b=new Node{id=3};a.next=b;if(!collect(a).Select(n=>n.id).SequenceEqual(new[]{1,2,3}))throw new Exception("Missing/order");});
        test("Native ID duplicate reference processed once",()=>{var b=new Node{id=2};var a=new Node{id=1,child=b,next=b};if(collect(a).Count!=2)throw new Exception("Duplicate");});
        test("Sibling cycle rejected",()=>{var a=new Node{id=1};a.next=a;try{collect(a);}catch(InvalidOperationException){return;}throw new Exception("Cycle accepted");});
        test("Missing exact native membership rejected",()=>{try{UniqueCached(new[]{1,2},v=>v==3,"body");}catch(InvalidOperationException){return;}throw new Exception("Missing accepted");});
        test("Ambiguous native membership rejected",()=>{try{UniqueCached(new[]{1,1},v=>v==1,"body");}catch(InvalidOperationException){return;}throw new Exception("Ambiguity accepted");});
        test("Four bodies sharing two Cut Lists retained",()=>{var rows=new[]{new{body="10-1",folder="10"},new{body="9-1",folder="9"},new{body="9-2",folder="9"},new{body="9-3",folder="9"}};var results=rows.Select(r=>UniqueCached(rows,c=>c.body==r.body,r.body)).ToArray();if(results.Length!=4||results.Count(r=>r.folder=="9")!=3)throw new Exception("CutList deduplication");});
        test("Native membership not inferred from a name",()=>{var candidates=new[]{new{label="same-name",identity=1},new{label="same-name",identity=2}};if(UniqueCached(candidates,c=>c.identity==2,"same-name").identity!=2)throw new Exception("Name inferred");});
        test("Traversal ID calls linear, no equality delegate",()=>{Node first=null;for(int i=99;i>=0;i--)first=new Node{id=i,next=first};int calls=0;var rows=Collect(first,n=>n.next,n=>n.child,n=>n.next,n=>{calls++;return n.id;},100);if(rows.Count!=100||calls!=100)throw new Exception("Nonlinear traversal");});
        test("Legacy quadratic feature comparisons reproduced offline",()=>{var roots=Enumerable.Range(0,100).Select(i=>new Node{id=i}).ToList();int comparisons=0;var chain=new List<Node>();foreach(var node in roots){if(chain.Any(x=>{comparisons++;return x.id==node.id;}))throw new Exception("Unexpected duplicate");chain.Add(node);}var all=CutListCoreV01.Traverse(chain,n=>new Node[0],(a,b)=>{comparisons++;return a.id==b.id;});if(all.Count!=100||comparisons!=9900)throw new Exception("Legacy call model differs: "+comparisons);});
        Console.WriteLine("Macro binding core: "+count+"/"+count+" PASS; no COM");return 0;
    }
}
