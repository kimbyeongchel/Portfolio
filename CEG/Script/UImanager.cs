using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UImanager : MonoBehaviour
{
    public static UImanager instance
    {
        get
        {
            if(m_instance == null)
                m_instance = FindObjectOfType<UImanager>();
            return m_instance;
        }
    }

    private static UImanager m_instance;

    public GameObject[] hearts;
    public Text correct;
    public Text numText;
    public Text problemText;
    public GameObject pNumberUI;
    public GameObject problemUI;
    public GameObject endUI;
    public Slider monsterHealth;

    public void UpdateCorrect(int correctCount)
    {
        correct.text = "맞힌 문제 수 = " + correctCount;
    }

    public void UpdateNum(int num)
    {
        numText.text = "Problem " + num;
    }

    public void UpdateProblem(string problem)
    {
        problemText.text = problem;
    }

    public void SetActivePNum(bool active)
    {
        pNumberUI.SetActive(active);
    }

    public void SetActiveProblem(bool active)
    {
        problemUI.SetActive(active);
    }

    public void SetActiveEnd(bool active)
    { 
        endUI.SetActive(active);
    }

    public void GameReStart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
