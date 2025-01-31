using UnityEngine;
using UnityEngine.UIElements;

public class InGameHud : MonoBehaviour
{
    private UIDocument _document;
    private VisualElement _root;
    private ProgressBar _healthBar;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _root = _document.rootVisualElement;

        _healthBar = _root.Q("HealthBar") as ProgressBar;
    }

    public void SetTargetPlayer(Player player)
    {
        _healthBar.highValue = player.HealthMax;
        _healthBar.value = player.Health;

        _healthBar.ClearBindings();
        _healthBar.SetBinding("highValue", new DataBinding
        {
            dataSource = player,
            dataSourcePath = new(nameof(Player.HealthMax)),
        });
        _healthBar.SetBinding("value", new DataBinding
        {
            dataSource = player,
            dataSourcePath = new(nameof(Player.Health)),
        });
    }
}
