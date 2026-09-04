using TMPro;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Electrical;

namespace RoomPlanner.Tools
{
    /// <summary>Dimensions follow the owner's current geometry, not a frozen pair of points.</summary>
    public sealed class ServiceDimensionDisplay : MonoBehaviour
    {
        public Material Material;
        private PlumbingObject _plumbing;
        private ElectricFixture _electric;
        private LineRenderer[] _lines;
        private TMP_Text[] _labels;
        private string[] _values = new string[3];
        private readonly float[] _numbers = { float.NaN, float.NaN, float.NaN };
        private Transform _head;
        private bool _initialized;

        private void Ensure()
        {
            if (_initialized) return;
            _initialized = true;
            _plumbing = GetComponent<PlumbingObject>(); _electric = GetComponent<ElectricFixture>();
            _head = Camera.main != null ? Camera.main.transform : null;
            _lines = new LineRenderer[3]; _labels = new TMP_Text[3];
            for (int i=0;i<3;i++)
            {
                var go = new GameObject("Dimension " + i); go.transform.SetParent(transform,false);
                var line = go.AddComponent<LineRenderer>(); line.useWorldSpace=true;line.positionCount=4;line.widthMultiplier=.002f;
                line.sharedMaterial=Material;_lines[i]=line;
                var label = new GameObject("Value");label.transform.SetParent(go.transform,false);
                var text=label.AddComponent<TextMeshPro>();text.fontSize=.09f;text.alignment=TextAlignmentOptions.Center;
                text.color=UiTokens.LabelLight;text.rectTransform.sizeDelta=new Vector2(.6f,.06f);_labels[i]=text;
            }
        }

        private void LateUpdate()
        {
            bool show = (_plumbing != null && _plumbing.ShowDimensions) || (_electric != null && _electric.ShowDimensions);
            if (!_initialized)
            {
                _plumbing=GetComponent<PlumbingObject>();_electric=GetComponent<ElectricFixture>();
                show=(_plumbing!=null&&_plumbing.ShowDimensions)||(_electric!=null&&_electric.ShowDimensions);
                if(!show)return;
                Ensure();
            }
            for(int i=0;i<3;i++)_lines[i].gameObject.SetActive(show);
            if(!show)return;
            if(_plumbing!=null&&_plumbing.IsPipe)
            {
                var p=_plumbing.Pipe;var first=p.Points[0];var last=p.Points[p.Points.Count-1];
                Draw(0,first,last,Vector3.up*.08f,"Route",WireMath.PolylineLength(p.Points),"m");
                var axis=(p.Points[1]-first).normalized;
                var cross=Vector3.Cross(axis,Mathf.Abs(axis.y)<.9f?Vector3.up:Vector3.right).normalized;
                Draw(1,first-cross*p.Dimensions.OuterDiameter*.5f,first+cross*p.Dimensions.OuterDiameter*.5f,
                    Vector3.up*.16f,"OD",p.Dimensions.OuterDiameter*1000,"mm");
                Draw(2,first,new Vector3(first.x,last.y,first.z),Vector3.right*.12f,
                    "Fall",(first.y-last.y)*100,"cm");
                return;
            }
            Vector3 size;Vector3 minimum;float level;
            if(_electric!=null){size=new Vector3(_electric.BlockWidth,_electric.BlockHeight,.02f);minimum=new Vector3(-size.x*.5f,-size.y*.5f,0);level=_electric.BaseLevel;}
            else
            {
                size=_plumbing.Fixture.Size;level=_plumbing.Fixture.BaseLevel;
                minimum=PlumbingCatalog.WallMounted(_plumbing.Fixture.Kind)?new Vector3(-size.x*.5f,-size.y*.5f,0):new Vector3(-size.x*.5f,0,-size.z*.5f);
            }
            Vector3 a=transform.TransformPoint(minimum);
            Draw(0,a,transform.TransformPoint(minimum+Vector3.right*size.x),-transform.up*.06f,"W",size.x*100,"cm");
            Draw(1,a,transform.TransformPoint(minimum+Vector3.up*size.y),-transform.right*.06f,"H",size.y*100,"cm");
            if(_electric!=null)
                Draw(2,new Vector3(transform.position.x,level,transform.position.z),transform.position,
                    transform.right*(size.x*.5f+.12f),"Center",(transform.position.y-level)*100,"cm");
            else Draw(2,a,transform.TransformPoint(minimum+Vector3.forward*size.z),-transform.right*.12f,"D",size.z*100,"cm");
        }

        private void Draw(int index,Vector3 a,Vector3 b,Vector3 offset,string caption,float number,string unit)
        {
            var line=_lines[index];line.SetPosition(0,a);line.SetPosition(1,a+offset);line.SetPosition(2,b+offset);line.SetPosition(3,b);
            var label=_labels[index];label.transform.position=(a+b)*.5f+offset;
            if(_head!=null)label.transform.rotation=Quaternion.LookRotation(label.transform.position-_head.position);
            number=Mathf.Round(number*100f)/100f;
            if(_numbers[index]!=number||_values[index]!=caption)
            {_numbers[index]=number;_values[index]=caption;label.text=$"{caption} {number:0.##} {unit}";}
        }
    }
}
