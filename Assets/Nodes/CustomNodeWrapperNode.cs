using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Nodeplay.Interfaces;
using Nodeplay.Engine;
using System;
using System.Reflection;
using System.Xml;
using Nodeplay.Core;

namespace Nodeplay.Nodes
{
		//this class describes a custom node as a nodemodel
		//it will be created at runtime from some customnodefunction description and points to some customnode graphmodel
		public class CustomNodeWrapper : DelegateNodeModel
		{
				
				//the custom node model class holds these values even outside of it's own evaluation scope
				//since we're not sure when the custom node definitio will be done executing and may require access to these values
				public List<Tuple<string,object>> Inputdata{ get; set; }

				public List<Tuple<string,Action>> Executiondata{ get; set; }
				//this static property is injected by reflection.emit when this type is built on loading a custom node
				public static Guid FunctiondefinitionID;	
				//TODO
				//it's possible we'll want to inject these as well so that they can be grabbed from the type for display in the library?
				public string Description { get; set; }

				public string Category { get; set; }

				public CustomNodeFunctionDescription Funcdef;

				public event Action Disposed;

				protected override void Start ()
				{
						base.Start ();
			
						//on start go grab the correct information from the appmodel's customNodeManager dict of loadead custom nodes
						
						CustomNodeFunctionDescription _funcdef;
						GameObject.FindObjectOfType<AppModel> ().CollapsedCustomGraphNodeManager.TryGetFunctionDefinition (FunctiondefinitionID, false, out _funcdef);
						if (_funcdef != null) {

								Funcdef = _funcdef;		
						}
						ValidateDefinition (Funcdef);

			
						//on start add a correctly named input port for each 
						//parameter in methodinfo that we've loaded
			
						foreach (var param in Funcdef.Parameters) {
								AddInputPort (param.Second.First);
						}
						//add an output port for each outputnode in the customnode graph
						foreach (var dataOutput in Funcdef.OutputNodes) {
								AddOutPutPort (dataOutput.Symbol);
						}

						//add an execution trigger for each inputexec node in the customnode graph
						if (Funcdef.InputExecutionNodes.Count > 1) {
								Debug.LogException (new ArgumentException ("customnodes cannot yet have more than 1 input trigger"));
				          
						}

						foreach (var inTrigger in Funcdef.InputExecutionNodes) {
								AddExecutionInputPort (inTrigger.Symbol);
						}
						//add an execution trigger out for each outputexec node in the customnode graph
						foreach (var outTrigger in Funcdef.OutputExecutionNodes) {
								AddExecutionOutPutPort (outTrigger.Symbol);
						}

						//we do not need an evaluator, there is nothing to evaluate...but im going to try using the c# evaluator
						//to push a task into the Q.
						//instead we'll directly compose our scope for evaluation on this node
						//gathering all inputs and storing them on the nodemodel (customnode)
						//then triggering our pointer into the customnode graph
						//the inputNodes in the graph will search the customnodeModel's inputdata
						//I can't think of another way to provide the inputs to the nodes in a clean way
						CodePointer = CompiledNodeEval;
						Evaluator = this.gameObject.AddComponent<CsharpEvaluator> ();

				}

				public override GameObject BuildSceneElements ()
				{
						var tempUI = base.BuildSceneElements ();
						tempUI.GetComponent<Renderer> ().material.color = Color.red;
						//var view = this.GetComponent("NodeView");

						return tempUI;
			
			
				}
				/// <summary>
				/// this method is used to force a regathering of execution data for immediate execution
				/// of a node, use case is a custom node wrapper about to execute a trigger on a node
				/// like this inputExecNode, we must gather the correct state of the node triggers
				/// and the currently executing tasks in the schedule, to correctly schedule any tasks
				/// the execution of this node generates... this is ugly and will be refactored later TODO
				/// </summary>
				public void ForceGatherExecutionData ()
				{
						Executiondata = gatherExecutionData ();
				}

				protected override void OnNodeModified ()
				{
						throw new NotImplementedException ();
				}

				protected virtual void OnDestroy ()
				{
						Disposed ();
				}
				
				internal override void Evaluate ()
				{
						OnEvaluation ();
						//build packages for all data 
						Inputdata = gatherInputPortData ();
						if (CodePointer == null) {
								Debug.Break ();
						}
					
					
						//on this node we actually want to gather execution data for the inputexecutionNode's output port
						//this is so that upon the execution of this node, we insert a task which executes whatever node that inputrigger
						//points to...
					
						//TODOneed to watch out for this, I believe the issue is that we need the invariant, that when the execution data is calculated
						//we're executing, because we also gather the current task... we need to atleast make sure we update the current task
						//at the time we do the insertion of the task.... 
						//a fix for now is to at the minimum create a property publicly reexecutes gather execution data
						//on the downstream node we really want to gather...
						Funcdef.InputExecutionNodes.First ().ForceGatherExecutionData ();
						Executiondata = Funcdef.InputExecutionNodes.First ().Executiondata;
						Debug.Log ("we're grabbing execution pointers not from this node, but from downstream");
						Executiondata.ForEach (x => Debug.Log (x.First));
						//here we use the codepointer instead of the code string
						var evalpackage = new EvaluationPackage (CodePointer,
					                                        Inputdata.Select (x => x.First).ToList (),
					                                        Inputdata.Select (x => x.Second).ToList (),
					                                        Outputs.Select (x => x.NickName).ToList (),
					                                        Executiondata);
						var outvar = Evaluator.Evaluate (evalpackage);
						this.StoredValueDict = outvar;
						OnEvaluated ();
					
				}
				
				protected override  Dictionary<string,object> CompiledNodeEval (Dictionary<string,object> inputstate, Dictionary<string,object> intermediateOutVals)
				{
						Debug.Log ("the calling instance is" + this.name);
						//for now only allow 1 execution input to simplify things
						Debug.Log ("about to call custom node graph:" + Funcdef.InputExecutionNodes [0].Symbol); 
						//now call the method on the "start" execution trigger
						//TODO make this a public method on the inputexecutionnode
						Funcdef.InputExecutionNodes [0].CustomNodeWrapperCaller = this;
						//I think instead of calling this pointer directly, we want to insert this as a task...lets try it
						//Funcdef.InputExecutionNodes[0].pointerToFirstNodeInGraph.DynamicInvoke();
						var triggerName = Funcdef.InputExecutionNodes.First ().Symbol;
						(inputstate ["done"] as Action).Invoke ();
						return intermediateOutVals;
				}


		#region customnode controller merged some methods into the wrapper
				public virtual void SyncNodeWithDefinition (NodeModel model)
				{
						if (IsInSyncWithNode (model)) 
								return;
					
						SyncNodeWithDefinition (model);
					
						this.OnNodeModified ();
				}
				
				
				/// <summary>
				///   Return if the custom node instance is in sync with its definition.
				///   It may be out of sync if .dyf file is opened and updated and then
				///   .dyn file is opened. 
				/// </summary>
				public bool IsInSyncWithNode (NodeModel model)
				{
						if (Funcdef == null)
								return true;
					
						if (Funcdef.Parameters != null) {
								var defParamNames = Funcdef.Parameters.Select (p => p.Second.First);
								var paramNames = this.Inputs.Select (p => p.NickName);
								if (!defParamNames.SequenceEqual (paramNames))
										return false;
							
								//TODO eventually add a check if the type has changed.
								//var defParamTypes = Funcdef.Parameters.Select (p => p.Second.ParameterType.ToString());
								//var paramTypes = this.Inputs.Select (p => p.sec);
								//if (!defParamTypes.SequenceEqual (paramTypes))
								//		return false;
						}
					
						if (Funcdef.ReturnKeys != null) {
								var returnKeys = this.Outputs.Select (p => p.NickName);
								if (!Funcdef.ReturnKeys.SequenceEqual (returnKeys))
										return false;
						}
					
						return true;
				}
		#endregion	

		#region Serialization/Deserialization methods
		
				public override void Save (XmlDocument doc, XmlElement element)
				{
						base.Save (doc, element); //Base implementation must be called
			
						
						
						XmlElement outEl = element.OwnerDocument.CreateElement ("ID");
			
						outEl.SetAttribute ("value", Funcdef.FunctionId.ToString ());
						element.AppendChild (outEl);
						element.SetAttribute ("nickname", name);

						var xmlDoc = element.OwnerDocument;
			
						outEl = xmlDoc.CreateElement ("Name");
						outEl.SetAttribute ("value", name);
						element.AppendChild (outEl);
			
						outEl = xmlDoc.CreateElement ("Description");
						outEl.SetAttribute ("value", Description);
						element.AppendChild (outEl);
			
						outEl = xmlDoc.CreateElement ("Inputs");
						foreach (string input in Inputs.Select(x => x.NickName)) {
								XmlElement inputEl = xmlDoc.CreateElement ("Input");
								inputEl.SetAttribute ("value", input);
								outEl.AppendChild (inputEl);
						}
						element.AppendChild (outEl);
			
						outEl = xmlDoc.CreateElement ("Outputs");
						foreach (string output in Outputs.Select(x => x.NickName)) {
								XmlElement outputEl = xmlDoc.CreateElement ("Output");
								outputEl.SetAttribute ("value", output);
								outEl.AppendChild (outputEl);
						}
						element.AppendChild (outEl);
				}
			
				public override void Load (XmlNode node)
				{
						base.Load (node); //Base implementation must be called
				
						List<XmlNode> childNodes = node.ChildNodes.Cast<XmlNode> ().ToList ();
				
						XmlNode nameNode = childNodes.LastOrDefault (subNode => subNode.Name.Equals ("Name"));
						if (nameNode != null && nameNode.Attributes != null)
								name = nameNode.Attributes ["value"].Value;
				
						XmlNode descNode = childNodes.LastOrDefault (subNode => subNode.Name.Equals ("Description"));
						if (descNode != null && descNode.Attributes != null)
								Description = descNode.Attributes ["value"].Value;
				
						// TODO I'm unsure if this is needed, if we load a node, won't it add it's output and input ports
						// onstart() when it loads its functiondefinition....
						/*	if (!IsInSyncWithNode (this)) {
								SyncNodeWithDefinition (this);
								//OnNodeModified ();
						} else {
								foreach (XmlNode subNode in childNodes) {
										if (subNode.Name.Equals ("Outputs")) {
												var data =
								subNode.ChildNodes.Cast<XmlNode> ()
									.Select (
										(outputNode) =>
										{
									AddInputPort(outputNode.Attributes[0].Value);
									});
										} else if (subNode.Name.Equals ("Inputs")) {
												var data =
								subNode.ChildNodes.Cast<XmlNode> ()
									.Select (
										(inputNode) =>
									{
									AddInputPort(inputNode.Attributes [0].Value);
									});
										}
						}
				}*/
				}
			#endregion
			
				private void ValidateDefinition (CustomNodeFunctionDescription def)
				{
						if (def == null) {
								Debug.LogException (new ArgumentNullException ("functiondef is null"));
						}
				
						if (def.IsProxy) {
								Debug.LogException (new ArgumentNullException ("custom node cannot be loaded"));
						}
						
				}
			
				public void ResyncWithDefinition (CustomNodeFunctionDescription def)
				{
						ValidateDefinition (def);
						Funcdef = def;
						SyncNodeWithDefinition (this);
				}
		}
		
		
		

}

