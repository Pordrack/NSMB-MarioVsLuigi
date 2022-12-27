using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace NSMB.Rebinding {
    public class RebindManager : MonoBehaviour {

        //---Serialized Variables
        [SerializeField] internal GameObject rebindPrompt;
        [SerializeField] internal TMP_Text rebindPromptText, rebindCountdownText;
        [SerializeField] private InputActionAsset controls;
        [SerializeField] private GameObject headerTemplate, buttonTemplate, axisTemplate, playerSettings, resetAll;
        [SerializeField] private Toggle fireballToggle, autosprintToggle;

        //---Private Variablse
        private readonly List<RebindButton> buttons = new();

        public void Init() {
            buttonTemplate.SetActive(false);
            axisTemplate.SetActive(false);
            headerTemplate.SetActive(false);

            LoadBindings();
            CreateActions();
            resetAll.transform.SetAsLastSibling();
        }

        public void LoadBindings() {
            string json = GlobalController.Instance.controlsJson;

            if (!string.IsNullOrEmpty(json)) {
                // we have old bindings...
                controls.LoadBindingOverridesFromJson(json);

            } else if (ControlSystem.file.Exists) {
                //load bindings...
                try {
                    controls.LoadBindingOverridesFromJson(File.ReadAllText(ControlSystem.file.FullName));
                    GlobalController.Instance.controlsJson = controls.SaveBindingOverridesAsJson();
                } catch (Exception e) {
                    Debug.LogError(e.Message);
                }
            }
        }

        public void ResetActions() {
            controls.RemoveAllBindingOverrides();

            foreach (RebindButton button in buttons) {
                button.targetBinding = button.targetAction.bindings[button.index];
                button.UpdateText();
            }

            Settings.Instance.fireballFromSprint = fireballToggle.isOn = true;
            Settings.Instance.autoSprint = autosprintToggle.isOn = false;
            Settings.Instance.SaveSettingsToPreferences();
            SaveRebindings();
        }

        private void CreateActions() {

            foreach (InputActionMap map in controls.actionMaps) {

                //controls starting with '!' are not rebindable
                if (map.name.StartsWith("!"))
                    continue;

                GameObject newHeader = Instantiate(headerTemplate);
                newHeader.name = map.name;
                newHeader.SetActive(true);
                newHeader.transform.SetParent(transform, false);
                newHeader.GetComponentInChildren<TMP_Text>().text = map.name;

                foreach (InputAction action in map.actions) {

                    //controls starting with '!' are not rebindable
                    if (action.name.StartsWith("!"))
                        continue;

                    if (action.bindings[0].isComposite) {
                        //axis
                        GameObject newTemplate = Instantiate(axisTemplate);
                        newTemplate.name = action.name;
                        newTemplate.transform.SetParent(transform, false);
                        newTemplate.SetActive(true);
                        RebindControl control = newTemplate.GetComponent<RebindControl>();
                        control.text.text = action.name;

                        int buttonIndex = 0;
                        for (int i = 0; i < action.bindings.Count; i++) {
                            InputBinding binding = action.bindings[i];
                            if (binding.isComposite)
                                continue;

                            RebindButton button = control.buttons[buttonIndex];
                            button.targetAction = action;
                            button.targetBinding = binding;
                            button.index = i;
                            button.manager = this;
                            buttonIndex++;

                            buttons.Add(button);
                        }
                    } else {
                        //button
                        GameObject newTemplate = Instantiate(buttonTemplate);
                        newTemplate.name = action.name;
                        newTemplate.transform.SetParent(transform, false);
                        newTemplate.SetActive(true);
                        RebindControl control = newTemplate.GetComponent<RebindControl>();
                        control.text.text = action.name;
                        for (int i = 0; i < control.buttons.Length; i++) {
                            RebindButton button = control.buttons[i];
                            button.targetAction = action;
                            button.targetBinding = action.bindings[i];
                            button.index = i;
                            button.manager = this;

                            buttons.Add(button);
                        }
                    }
                }

                if (map.name == "Player")
                    playerSettings.transform.SetAsLastSibling();
            }
        }

        public void SaveRebindings() {
            string json = controls.SaveBindingOverridesAsJson();

            if (!ControlSystem.file.Exists)
                ControlSystem.file.Directory.Create();

            File.WriteAllText(ControlSystem.file.FullName, json);
            GlobalController.Instance.controlsJson = json;
        }
    }
}
