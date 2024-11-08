using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager instance
    {
        get
        {
            if (m_instance == null)
                m_instance = FindObjectOfType<GameManager>();
            return m_instance;
        }
    }

    private static GameManager m_instance;

    private Quiz quiz;
    private UImanager ui;
    private int currentIndex;
    private Player player;
    private Monster monster;
    private Living living;
    private int countCorrect;
    private int nonCorrect;
    public InputField answer;

    private void Awake()
    {
        if (instance != this)
        {
            Destroy(gameObject);
        }

        currentIndex = 0;
        countCorrect = 0;
        nonCorrect = 0;

        ui = UImanager.instance;
        quiz = FindObjectOfType<Quiz>();
        monster = FindObjectOfType<Monster>();
        player = FindObjectOfType<Player>();
        living = FindObjectOfType<Living>();

        quiz.AddProblem();
        quiz.SelectRandomProblems(5);
 
        StartCoroutine(ShowProblem());
    }

    private void Update()
    {
        if((currentIndex >= 5) || (living.dead == true))
        {
            Invoke("ShowEnd", 2f);
            return;
        }
        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (string.IsNullOrEmpty(answer.text))
            {
                return;
            }
            CheckAnswer();
        }
    }

    private void ShowEnd()
    {
        ui.SetActiveEnd(true);
        UImanager.instance.monsterHealth.gameObject.SetActive(false);
        if (ui.hearts != null)
        {
            foreach (GameObject heart in ui.hearts)
            {
                Destroy(heart);
            }
        }
    }

    private void CheckAnswer()
    {
        string inputAnswer = answer.text;
        string currentAnswer = quiz.selectedProblems[quiz.problemKeys[currentIndex]].ToString();

        if (inputAnswer.Equals(currentAnswer))
        {
            ui.problemText.color = Color.green;
            monster.TakeDamage(20f);
            ui.UpdateCorrect(++countCorrect);
        }
        else
        {
            ui.problemText.color = Color.red;
            player.TakeDamage(1f);
            Destroy(ui.hearts[nonCorrect++].gameObject);
        }

        currentIndex++;
        answer.text = "";

        Invoke("ShowProblemCoroutine", 1f);
    }

    private void ShowProblemCoroutine()
    {
        StartCoroutine(ShowProblem());
    }

    private IEnumerator ShowProblem()
    {
        string currentProblem = quiz.problemKeys[currentIndex];

        yield return new WaitForSeconds(1f);
        ui.problemText.color = Color.white;
        ui.SetActiveProblem(false);
        ui.UpdateNum(currentIndex + 1);
        ui.UpdateProblem(currentProblem);
        ui.SetActivePNum(true);
        yield return new WaitForSeconds(1f);
        ui.SetActivePNum(false);
        ui.SetActiveProblem(true);
    }
}