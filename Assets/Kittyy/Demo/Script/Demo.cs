
using UnityEngine;
using UnityEngine.UI;

namespace myDemo {

    public class Demo : MonoBehaviour {

        public AnimationClip[] animatorClip;
        Button button;
        int index1;
        public Animator[] animator;
        string animationName;

        void Start() {
            button = GameObject.Find("Button").GetComponent<Button>();
            button.onClick.AddListener(Push);
        }

        void Push() {
            index1++;
            if (index1 >= animatorClip.Length) {
                index1 = 0;
            }
            animationName = animatorClip[index1].ToString().Substring(0, animatorClip[index1].ToString().IndexOf(" "));
            foreach (Animator m in animator) {
                m.CrossFadeInFixedTime(animationName, 0);
            }

        }

        // Update is called once per frame
        void Update() {
        }
    }
}







