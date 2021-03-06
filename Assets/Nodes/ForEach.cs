using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Nodeplay.Interfaces;
using Nodeplay.Engine;
using System;
using Nodeplay.UI;

namespace Nodeplay.Nodes
{
	public class ForEach : NodeModel
	{
		
		
		protected override void Start()
		{
			base.Start();
			AddOutPutPort("OUTPUT");
			AddInputPort("input1");
			
			AddExecutionInputPort("start");
			
			AddExecutionOutPutPort("onIteration");
			AddExecutionOutPutPort("onIterated");
			
			Code = @"for i in input1:
	OUTPUT = i
	onIteration()
onIterated()";
			
			//Code = "for i in range(input1*2):" +Environment.NewLine +
			//		"\t"+"onIteration()" + Environment.NewLine+
			
			Evaluator = this.gameObject.AddComponent<PythonEvaluator>();
		}
		
		public override GameObject BuildSceneElements()
		{
			var tempUI = base.BuildSceneElements();
			tempUI.GetComponent<Renderer>().material.color = Color.cyan;
			tempUI.AddComponent<BoundingRenderer>();
			tempUI.GetComponent<BoundingRenderer> ().initialze (new List<int> (){0}, new List<Color>() {Color.cyan});
			return tempUI;
			
		}
		
		
		
	}
}