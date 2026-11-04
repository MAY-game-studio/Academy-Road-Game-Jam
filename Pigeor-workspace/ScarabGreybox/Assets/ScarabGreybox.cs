using UnityEngine;
using System.Collections.Generic;

// Standalone, asset-free greybox. Open any scene and enter Play mode.
public class ScarabGreybox : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (!FindObjectOfType<ScarabGreybox>()) new GameObject("Scarab Greybox").AddComponent<ScarabGreybox>(); }
    public float moveSpeed=6, acceleration=5, pushSpeed=10, turnSpeed=140, growCost=20, shotSpeed=17, trailLife=12;
    struct Trail { public Vector2 a,b; public float end; public GameObject view; }
    class Target { public GameObject view; public Vector2 pos; public int hp=3; public float hit; public bool button; }
    readonly List<GameObject> dust=new List<GameObject>();
    readonly List<Rect> walls=new List<Rect>();
    readonly List<Trail> trails=new List<Trail>();
    readonly List<Target> targets=new List<Target>();
    GameObject player,ball,projectile,gate; Sprite square,circle; Camera cam;
    Vector2 pos,heading=Vector2.right,velocity,shotPos,shotVelocity,safe;
    float charge,speed,shotCharge,shotEnd,buttonDeadline,hitUntil; int bounces; bool pushing,wasPush,won;
    readonly Rect gap=new Rect(7,-7,3,14);
    void Start() {
        square=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,1,1),new Vector2(.5f,.5f),1);
        var tex=new Texture2D(32,32); for(int y=0;y<32;y++) for(int x=0;x<32;x++) tex.SetPixel(x,y,Vector2.Distance(new Vector2(x,y),new Vector2(15.5f,15.5f))<15.5f?Color.white:Color.clear); tex.Apply();
        circle=Sprite.Create(tex,new Rect(0,0,32,32),new Vector2(.5f,.5f),32);
        cam=Camera.main; if(!cam) cam=new GameObject("Camera").AddComponent<Camera>(); cam.orthographic=true; cam.orthographicSize=11; cam.transform.position=new Vector3(0,0,-10); cam.backgroundColor=new Color(.06f,.08f,.12f);
        ResetWorld();
    }
    GameObject Shape(string name,Vector2 p,Vector2 scale,Color color,bool round=false,int order=0) {
        var go=new GameObject(name); go.transform.SetParent(transform); go.transform.position=p; go.transform.localScale=scale; var r=go.AddComponent<SpriteRenderer>(); r.sprite=round?circle:square; r.color=color;r.sortingOrder=order;return go;
    }
    void Wall(Rect r) { walls.Add(r); Shape("Wall",r.center,r.size,new Color(.35f,.4f,.47f)); }
    void ResetWorld() {
        foreach(Transform t in transform) Destroy(t.gameObject); dust.Clear();walls.Clear();trails.Clear();targets.Clear(); projectile=null;charge=0;speed=0;won=false;pushing=wasPush=false;buttonDeadline=0;pos=safe=new Vector2(-15,0);heading=Vector2.right;
        Shape("Floor",Vector2.zero,new Vector2(40,20),new Color(.13f,.18f,.2f),false,-5);
        Shape("Gap - trail required",gap.center,gap.size,Color.black,false,-3);
        Wall(new Rect(-20,-10,40,1));Wall(new Rect(-20,9,40,1));Wall(new Rect(-20,-10,1,20));Wall(new Rect(19,-10,1,20));
        Wall(new Rect(-7,-5,1,6));Wall(new Rect(-1,2,1,6));Wall(new Rect(4,-8,1,4));
        for(int i=0;i<75;i++) SpawnDust(new Vector2(Random.Range(-17f,5f),Random.Range(-8f,8f)));
        for(int i=0;i<3;i++) {Vector2 p=new Vector2(12+i*2,4); targets.Add(new Target{pos=p,button=true,view=Shape("Timed button",p,Vector2.one,Color.cyan,true)});}
        for(int i=0;i<7;i++){Vector2 p=new Vector2(-11+i*2,-3);targets.Add(new Target{pos=p,view=Shape("Damage target",p,Vector2.one,Color.red)});}
        gate=Shape("Gate",new Vector2(16,0),new Vector2(1,5),Color.magenta);walls.Add(new Rect(15.5f,-2.5f,1,5));
        Shape("Finish",new Vector2(18,0),new Vector2(1,2),Color.green);
        player=Shape("Scarab",pos,new Vector2(.65f,.65f),Color.green,true,5);ball=Shape("Sun",pos+heading,Vector2.one,Color.yellow,true,4);
    }
    void SpawnDust(Vector2 p) { if(Blocked(p,.15f)||gap.Contains(p))return;dust.Add(Shape("Stardust",p,new Vector2(.18f,.18f),Color.yellow,true,1)); }
    bool Blocked(Vector2 p,float radius) { foreach(var r in walls) {var expanded=r;expanded.xMin-=radius;expanded.xMax+=radius;expanded.yMin-=radius;expanded.yMax+=radius;if(expanded.Contains(p))return true;} return false; }
    bool OnTrail(Vector2 p) {foreach(var t in trails) if(t.end>Time.time&&Distance(p,t.a,t.b)<.8f)return true;return false;}
    static float Distance(Vector2 p,Vector2 a,Vector2 b){var d=b-a;return Vector2.Distance(p,a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.0001f,d.sqrMagnitude)));}
    void Update() {
        if(Input.GetKeyDown(KeyCode.R)){ResetWorld();return;} if(Input.GetKeyDown(KeyCode.F))charge=growCost;if(Input.GetKeyDown(KeyCode.G))for(int i=0;i<20;i++)SpawnDust(pos+Random.insideUnitCircle*3);
        float dt=Mathf.Min(Time.deltaTime,.04f); bool held=Input.GetKey(KeyCode.Space);pushing=held&&!won;
        Vector2 input=new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical")).normalized;
        if(pushing){Vector2 aim=(Vector2)cam.ScreenToWorldPoint(Input.mousePosition)-pos;if(aim.sqrMagnitude>.1f)heading=Vector3.RotateTowards(heading,aim.normalized,turnSpeed*Mathf.Deg2Rad*Mathf.Lerp(1,.4f,charge/growCost)*dt,0);speed=Mathf.MoveTowards(speed,pushSpeed,acceleration*dt);velocity=heading*speed;}
        else {speed=0;velocity=input*moveSpeed;if(input.sqrMagnitude>0)heading=input;}
        if(wasPush&&!pushing&&!won) Fire();wasPush=pushing;
        Vector2 next=pos+velocity*dt*(OnTrail(pos)?1.6f:1); if(!Blocked(new Vector2(next.x,pos.y),.35f))pos.x=next.x;if(!Blocked(new Vector2(pos.x,next.y),.35f))pos.y=next.y;
        if(gap.Contains(pos)&&!OnTrail(pos))pos=safe;else if(!gap.Contains(pos))safe=pos;
        player.transform.position=pos; float radius=Mathf.Lerp(.35f,.85f,charge/growCost);Vector2 bp=pos+heading*(radius+.5f);ball.SetActive(pushing);ball.transform.position=bp;ball.transform.localScale=Vector2.one*radius*2;
        if(pushing) for(int i=dust.Count-1;i>=0;i--)if(Vector2.Distance(bp,dust[i].transform.position)<radius+.3f){charge=Mathf.Min(growCost,charge+1);speed*=.97f;Destroy(dust[i]);dust.RemoveAt(i);}
        if(pushing)Hit(bp,radius,charge>=growCost?3:1);
        if(projectile) StepShot(dt);
        for(int i=trails.Count-1;i>=0;i--){var t=trails[i]; if(Time.time>=t.end){Destroy(t.view);trails.RemoveAt(i);}else {var r=t.view.GetComponent<SpriteRenderer>();r.color=new Color(.2f,.65f,1,Mathf.Clamp01((t.end-Time.time)/2));}}
        if(buttonDeadline>0&&Time.time>buttonDeadline){foreach(var t in targets)if(t.button){t.hp=3;t.view.GetComponent<SpriteRenderer>().color=Color.cyan;}buttonDeadline=0;}
        bool all=true;foreach(var t in targets)if(t.button&&t.hp>0)all=false;if(all&&gate){Destroy(gate);walls.RemoveAt(walls.Count-1);buttonDeadline=0;}
        if(pos.x>17&&Mathf.Abs(pos.y)<2)won=true;
    }
    void Hit(Vector2 p,float radius,int damage){foreach(var t in targets)if(t.hp>0&&Time.time>t.hit&&Vector2.Distance(p,t.pos)<radius+.5f){t.hit=Time.time+.6f;t.hp-=t.button?3:damage;speed*=.75f;if(t.button&&buttonDeadline==0)buttonDeadline=Time.time+10;if(t.hp<=0)t.view.GetComponent<SpriteRenderer>().color=Color.gray;}}
    void Fire(){if(projectile)EndShot();shotCharge=charge;shotPos=pos+heading*1.5f;shotVelocity=heading*shotSpeed+velocity*.25f;shotEnd=Time.time+5;bounces=0;projectile=Shape("Flying sun",shotPos,Vector2.one*Mathf.Lerp(.7f,1.7f,charge/growCost),Color.yellow,true,4);charge=0;}
    void StepShot(float dt){int steps=Mathf.CeilToInt(shotVelocity.magnitude*dt/.15f);for(int i=0;i<steps&&projectile;i++){Vector2 old=shotPos,next=old+shotVelocity*dt/steps;float radius=Mathf.Lerp(.35f,.85f,shotCharge/growCost);if(Blocked(next,radius)){if(shotCharge<growCost||bounces>=3){EndShot();break;}bool x=Blocked(new Vector2(next.x,old.y),radius);bool y=Blocked(new Vector2(old.x,next.y),radius);if(x||!y)shotVelocity.x=-shotVelocity.x;if(y)shotVelocity.y=-shotVelocity.y;bounces++;continue;}shotPos=next;if(shotCharge>=growCost)AddTrail(old,next);Hit(shotPos,radius,1);projectile.transform.position=shotPos;}if(projectile&&Time.time>shotEnd)EndShot();}
    void AddTrail(Vector2 a,Vector2 b){var d=b-a;var view=Shape("Trail",(a+b)/2,new Vector2(d.magnitude+.05f,1.6f),new Color(.2f,.65f,1),false,-2);view.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg);trails.Add(new Trail{a=a,b=b,view=view,end=Time.time+trailLife});}
    void EndShot(){if(!projectile)return;for(int i=0;i<Mathf.FloorToInt(shotCharge*.5f);i++)SpawnDust(shotPos+Random.insideUnitCircle*1.5f);Destroy(projectile);projectile=null;}
    void OnGUI(){GUI.Box(new Rect(10,10,630,115),"SCARAB GREYBOX | WASD: walk | Hold SPACE + mouse: push/steer | Release: shoot\nR: reset | F: full charge | G: add dust\nCharge: "+charge+" / "+growCost+" | Speed: "+speed.ToString("F1")+" | Dust: "+dust.Count+"\nCross black gap on blue trail. Hit 3 cyan switches within 10s. Reach green exit."+(won?"\nCOMPLETE! Press R to replay.":""));}
}

