using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Quiz : MonoBehaviour
{
    public Dictionary<string, int> problem = new Dictionary<string, int>();
    public Dictionary<string, int> selectedProblems = new Dictionary<string, int>();
    public List<string> problemKeys = new List<string>();

    public void AddProblem()
    {
        problem.Add("각 런의 크기가 다음과 같을 때, Hufman 알고리즘에 의한 병합 트리의 깊이를 x, 최소 병합 비용을 y라고 하자. x + y의 값은 얼마인가?\n\n런의 크기: 2, 4, 7, 8, 12", 76);
        problem.Add("2-3-4 트리에서 \"q = 리프 노드의 수 / 전체 노드 수\"로 정의된다. 예를 들면, depth가 2이며 루트 노드가 2-노드인 경우, q = 2 / 3 이다. depth가 3인 2-3-4 트리에서 q의 최대값은 얼마인가?(분자분모식으로 적으세요 ex.12 = 1/2)", 1621);
        problem.Add("다음 중 텍스트 파일에서 단어의 빈도수를 저장하기에 가장 적합한 구조는 무엇인가?\n1) HashMap<String, Integer>\n2) TreeMap<String, Integer>\n3) ArrayList<String>\n4) PriorityQueue<String>\n5) HashSet<String, Integer>\r\n", 1);
        problem.Add("배열을 쉘 정렬을 이용하여 오름차순으로 정렬하고자 한다. h = 4일때, 결과는?\n초기값 [0] [1] [2] [3] [4] [5] [6] [7] [8] [9]\n \t\t\t\t20  5 12 17  8  10  4   7 13  2\n([2], [6], [8] 값을 순서대로 작성)", 41220);
        problem.Add("키 값이 입력될 때 생성되는 트리를 그리세요(level 순으로 순서대로 적기)\n8->9->10->1->2", 921018);
        problem.Add("여섯 개의 키에 대한 해쉬 함수 값과 실제 저장된 주소가 아래에 있다. 평균 탐색 거리는?\n해쉬값      A:1 B:1 C:2 D:2 E:3 F:2\n저장주소 A:1 B:2 C:3 D:4 E:5 F:6\n(ex.1/2 = 12)", 83);
        problem.Add("다음 중 여러 개의 해시 함수를 이용하여 해싱을 수행하는 알고리즘을 모두 선택하시오\n1) Linear probing\n2) Separate chaining\n3) Quadratic probing\n4) Double hashing\n5) Cuckoo hashing", 45);
        problem.Add("아래 패턴에 대해 NFA의 이동엣지 수를 구하라\n((A|B)|C*))", 12);
        problem.Add("패턴 AABCA에 대한 DFA를 작성하고 (가), (나)를 적으시오(1,2 -> 12)\n\rA A B C A\nA 1 2 2 1 5\nB 0 0 가 0 0\nC 0 나 0 4 0", 30);
        problem.Add("다음 중 컬렉션의 contains() 시간복잡도가 가장 빠른 것은?(자바)\n1) List\n2) Set\n3) TreeMap\n4) ArrayList\n5) TreeSet", 2);
    }

    public void SelectRandomProblems(int count)
    {
        selectedProblems.Clear();

        List<string> keing = new List<string>(problem.Keys);

        for (int i = 0; i < count; i++)
        {
            int randomIndex = Random.Range(0, keing.Count);
            string selectedProblem = keing[randomIndex];
            int selectedAnswer = problem[selectedProblem];

            selectedProblems.Add(selectedProblem, selectedAnswer);
            keing.RemoveAt(randomIndex);
        }
                
        foreach(string prob in selectedProblems.Keys)
        {
            problemKeys.Add(prob);
        }

    }
}
